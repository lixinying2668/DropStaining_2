using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualBasic.FileIO;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Twin;

// 对应 stainer_twin_fastapi/stainer_twin_api/snapshot.py。
// 逐方法移植 Python 实现，关键不变量：
//  - 顶层 JSON 键是 snake_case 与 camelCase 混合，必须用字面量键的 Dictionary 构造、
//    以 PropertyNamingPolicy=null 序列化，避免键名漂移。
//  - 任何表/行/字段缺失都回退为 null（由 TwinSqlite 的 null-on-error 语义保证）。
// 注册为单例；注册表/映射资产通过嵌入资源一次性懒加载。
public sealed class TwinSnapshotService
{
    private static readonly Lazy<IReadOnlyList<RegistryEntry>> Registry = new(LoadRegistry, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<Dictionary<string, string>>> MappingRowsCache = new(LoadMappingRows, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<byte[]> MappingCsvCache = new(LoadMappingCsvBytes, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly JsonSerializerOptions LiteralJsonOptions = new() { PropertyNamingPolicy = null };

    private static readonly Dictionary<string, long> DrawerToChannel = new()
    {
        ["A"] = 1, ["B"] = 2, ["C"] = 3, ["D"] = 4
    };

    private static readonly HashSet<string> StatusComplete = new() { "completed", "complete", "succeeded", "success", "available", "stable", "confirmed", "waitingunload" };
    private static readonly HashSet<string> StatusRunning = new() { "running", "active", "inprogress", "processing" };
    private static readonly HashSet<string> StatusError = new() { "failed", "error", "faulted", "invalid" };
    private static readonly HashSet<string> StatusIdle = new() { "idle", "off", "ready", "normal" };

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private string? _connectionString;

    public TwinSnapshotService(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public static JsonSerializerOptions JsonOptions => LiteralJsonOptions;

    private string ConnectionString
        => _connectionString ??= DatabasePathResolver.ResolveConnectionString(_configuration, _environment);

    public Dictionary<string, object?> BuildSnapshot()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var scalars = BuildScalars(connection);
        var payload = new Dictionary<string, object?>
        {
            ["items"] = BuildItems(connection),
            ["slideTemps"] = BuildSlideTemps(connection),
            ["slideOps"] = BuildSlideOps(connection),
            ["channels"] = BuildChannels(connection),
            ["liquids"] = BuildLiquids(connection),
            ["metrics"] = BuildMetrics(connection),
            ["cameras"] = BuildCameras(connection),
        };

        var arm = BuildArmPayload(connection);
        if (arm is not null)
        {
            payload["arm"] = arm;
        }

        var profiles = BuildWorkflowProfiles(connection);
        if (profiles.Count > 0)
        {
            payload["configProfiles"] = profiles;
        }

        var (logs, warnings) = BuildLogs(connection);

        return new Dictionary<string, object?>
        {
            ["schema_version"] = 1,
            ["generated_at_utc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["null_policy"] = "数据库表/行/字段/JSON key 不存在时返回 null；前端不得再用随机数补值。",
            ["digitalTwinPayload"] = payload,
            ["scalars"] = scalars,
            ["precheckResults"] = BuildPrecheckResults(connection),
            ["control_values"] = BuildControlValues(scalars),
            ["logs"] = logs,
            ["warnings"] = warnings,
        };
    }

    public object? GetControlValue(string controlId)
    {
        var snapshot = BuildSnapshot();
        if (snapshot.TryGetValue("control_values", out var values) && values is Dictionary<string, object?> controlValues)
        {
            return controlValues.TryGetValue(controlId, out var value) ? value : null;
        }

        return null;
    }

    public IReadOnlyList<Dictionary<string, string>> GetMappingRows(string? status)
    {
        var rows = MappingRowsCache.Value;
        if (string.IsNullOrEmpty(status))
        {
            return rows;
        }

        return rows.Where(r => r.TryGetValue("link_status", out var linkStatus) && linkStatus == status).ToList();
    }

    public byte[] GetMappingCsv() => MappingCsvCache.Value;

    // ---- build_* 方法：对应 snapshot.py 中的同名函数 ----

    private Dictionary<string, Dictionary<string, object?>> LatestReagentPlacements(SqliteConnection connection)
    {
        var placements = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        if (!(TwinSqlite.HasColumns(connection, "reagent_rack_placements", "reagent_bottle_id", "reagent_rack_position_id", "removed_at_utc")
              && TwinSqlite.HasColumns(connection, "reagent_rack_positions", "id", "code", "position_no", "column_no", "row_no")
              && TwinSqlite.HasColumns(connection, "reagent_bottles", "id", "remaining_volume_ul", "initial_volume_ul", "status", "reagent_code", "full_barcode")))
        {
            return placements;
        }

        var rows = TwinSqlite.Many(connection, """
            SELECT p.code, p.position_no, p.column_no, p.row_no,
                   b.reagent_code, b.full_barcode, b.remaining_volume_ul, b.initial_volume_ul, b.status
            FROM reagent_rack_placements rp
            JOIN reagent_rack_positions p ON p.id = rp.reagent_rack_position_id
            JOIN reagent_bottles b ON b.id = rp.reagent_bottle_id
            WHERE rp.removed_at_utc IS NULL
            ORDER BY rp.placed_at_utc DESC
            """);

        foreach (var row in rows)
        {
            var name = ReagentFrontendName(Get(row, "position_no"), Get(row, "column_no"), Get(row, "row_no"));
            if (name is not null && !placements.ContainsKey(name))
            {
                placements[name] = row;
            }
        }

        return placements;
    }

    private List<object> BuildItems(SqliteConnection connection)
    {
        var items = new List<object>();
        var placements = LatestReagentPlacements(connection);

        foreach (var entry in Registry.Value)
        {
            if (entry.Category != "试剂区")
            {
                continue;
            }

            var name = entry.Name;
            if (name is not null && placements.TryGetValue(name, out var placement))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["state"] = StatusToFrontend(Get(placement, "status")),
                    ["level"] = Pct(Get(placement, "remaining_volume_ul"), Get(placement, "initial_volume_ul")),
                    ["reagentCode"] = Get(placement, "reagent_code"),
                    ["barcode"] = Get(placement, "full_barcode"),
                });
            }
            else
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["state"] = null,
                    ["level"] = null,
                    ["reagentCode"] = null,
                    ["barcode"] = null,
                });
            }
        }

        if (TwinSqlite.HasColumns(connection, "dab_mix_positions", "position_no", "status", "active_dab_batch_id"))
        {
            var activeBatches = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            if (TwinSqlite.HasColumns(connection, "dab_batches", "id", "remaining_volume_ul", "total_volume_ul", "expires_at_utc", "status"))
            {
                foreach (var batch in TwinSqlite.Many(connection, "SELECT * FROM dab_batches"))
                {
                    var id = Get(batch, "id")?.ToString();
                    if (id is not null)
                    {
                        activeBatches[id] = batch;
                    }
                }
            }

            foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM dab_mix_positions ORDER BY position_no"))
            {
                var name = MixFrontendName(Get(row, "position_no"));
                if (name is null)
                {
                    continue;
                }

                var batchId = Get(row, "active_dab_batch_id")?.ToString();
                var batch = batchId is not null && activeBatches.TryGetValue(batchId, out var b) ? b : null;
                items.Add(new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["state"] = StatusToFrontend(Get(row, "status")),
                    ["level"] = batch is not null ? Pct(Get(batch, "remaining_volume_ul"), Get(batch, "total_volume_ul")) : null,
                    ["validUntilUtc"] = batch is not null ? Get(batch, "expires_at_utc") : null,
                });
            }
        }

        foreach (var name in new[] { "A液", "B液" })
        {
            if (Registry.Value.Any(r => r.Name == name))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["state"] = null,
                    ["level"] = null,
                });
            }
        }

        return items;
    }

    private List<object> BuildSlideTemps(SqliteConnection connection)
    {
        var result = new List<object>();
        if (!TwinSqlite.HasColumns(connection, "thermal_point_states", "drawer_code", "slot_no", "current_temperature_deci_c", "target_temperature_deci_c", "status"))
        {
            return result;
        }

        foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM thermal_point_states"))
        {
            var name = SlideFrontendName(Get(row, "drawer_code"), Get(row, "slot_no"));
            if (name is null)
            {
                continue;
            }
            name = NormalizeSlideFrontendName(name);

            result.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["temp"] = DeciC(Get(row, "current_temperature_deci_c")),
                ["targetTemp"] = DeciC(Get(row, "target_temperature_deci_c")),
                ["state"] = StatusToFrontend(Get(row, "status")),
            });
        }

        return result;
    }

    private List<object> BuildSlideOps(SqliteConnection connection)
    {
        var result = new List<object>();
        if (!(TwinSqlite.HasColumns(connection, "slide_tasks", "physical_slot_id", "slot_code", "status")
              && TwinSqlite.HasColumns(connection, "physical_slots", "id", "code", "slot_no", "drawer_id")
              && TwinSqlite.HasColumns(connection, "drawers", "id", "code")))
        {
            return result;
        }

        var rows = TwinSqlite.Many(connection, """
            SELECT st.status, ps.slot_no, d.code AS drawer_code
            FROM slide_tasks st
            JOIN physical_slots ps ON ps.id = st.physical_slot_id
            JOIN drawers d ON d.id = ps.drawer_id
            """);

        foreach (var row in rows)
        {
            var name = SlideFrontendName(Get(row, "drawer_code"), Get(row, "slot_no"));
            if (name is null)
            {
                continue;
            }
            name = NormalizeSlideFrontendName(name);

            var done = StatusToFrontend(Get(row, "status")) == "complete";
            result.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["steps"] = new List<object> { done },
            });
        }

        return result;
    }

    private List<Dictionary<string, object?>> BuildChannels(SqliteConnection connection)
    {
        var channels = new List<Dictionary<string, object?>>();
        for (var i = 1; i <= 4; i++)
        {
            channels.Add(new Dictionary<string, object?>
            {
                ["id"] = i,
                ["state"] = null,
                ["progress"] = null,
                ["pulled"] = null,
                ["configProfileId"] = null,
            });
        }

        if (TwinSqlite.HasColumns(connection, "channel_batches", "drawer_code", "status", "selected_workflow_version_id"))
        {
            foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM channel_batches"))
            {
                var drawerCode = (Get(row, "drawer_code")?.ToString() ?? string.Empty).ToUpperInvariant();
                if (!DrawerToChannel.TryGetValue(drawerCode, out var channel) || channel == 0)
                {
                    continue;
                }

                var dict = channels[(int)(channel - 1)];
                var state = StatusToFrontend(Get(row, "status"));
                dict["state"] = state;
                dict["progress"] = state == "complete" ? (object?)100 : null;
                dict["configProfileId"] = Get(row, "selected_workflow_version_id");
                dict["experimentType"] = Get(row, "experiment_type");
            }
        }

        return channels;
    }

    private Dictionary<string, object?> BuildLiquids(SqliteConnection connection)
    {
        var result = new Dictionary<string, object?>
        {
            ["pure"] = null,
            ["pbs"] = null,
            ["waste"] = null,
            ["toxic"] = null,
        };

        if (!TwinSqlite.HasColumns(connection, "liquid_container_states", "source_type", "current_volume_ul", "capacity_ul"))
        {
            return result;
        }

        var keyBySource = new Dictionary<string, string>
        {
            ["SystemWater"] = "pure",
            ["PBS"] = "pbs",
            ["Waste"] = "waste",
            ["ToxicWaste"] = "toxic",
        };

        foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM liquid_container_states"))
        {
            var source = Get(row, "source_type")?.ToString();
            if (source is not null && keyBySource.TryGetValue(source, out var key))
            {
                result[key] = Pct(Get(row, "current_volume_ul"), Get(row, "capacity_ul"));
            }
        }

        return result;
    }

    private Dictionary<string, object?> BuildScalars(SqliteConnection connection)
    {
        var scalars = new Dictionary<string, object?>(StringComparer.Ordinal);

        var cooling = TwinSqlite.TableExists(connection, "cooling_unit_states")
            ? TwinSqlite.One(connection, "SELECT * FROM cooling_unit_states LIMIT 1")
            : null;
        scalars["reagent_current_temperature_c"] = cooling is not null ? DeciC(Get(cooling, "current_temperature_deci_c")) : null;
        scalars["reagent_target_temperature_c"] = cooling is not null ? DeciC(Get(cooling, "target_temperature_deci_c")) : null;
        scalars["reagent_cooling_status"] = cooling is not null ? Get(cooling, "status") : null;

        var isConnectedRaw = cooling is not null ? Get(cooling, "is_connected") : null;
        if (isConnectedRaw is null)
        {
            scalars["reagent_cooling_connected"] = null;
        }
        else
        {
            scalars["reagent_cooling_connected"] = (Int(isConnectedRaw) ?? 0) != 0;
        }

        var arm = TwinSqlite.TableExists(connection, "robot_arm_states")
            ? TwinSqlite.One(connection, "SELECT * FROM robot_arm_states LIMIT 1")
            : null;
        foreach (var axis in new[] { "x", "y", "z" })
        {
            var value = arm is not null ? Get(arm, $"current_{axis}_um") : null;
            scalars[$"arm_current_{axis}_mm"] = IsNumber(value) ? RoundDiv(value, 1000.0, 3) : null;
        }

        scalars["arm_status"] = arm is not null ? Get(arm, "status") : null;

        if (TwinSqlite.TableExists(connection, "liquid_container_states"))
        {
            var keyBySource = new Dictionary<string, string>
            {
                ["SystemWater"] = "pure",
                ["PBS"] = "pbs",
                ["Waste"] = "waste",
                ["ToxicWaste"] = "toxic",
            };
            foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM liquid_container_states"))
            {
                var source = Get(row, "source_type")?.ToString();
                if (source is null || !keyBySource.TryGetValue(source, out var key))
                {
                    continue;
                }

                scalars[$"{key}_current_volume_ul"] = Get(row, "current_volume_ul");
                scalars[$"{key}_capacity_ul"] = Get(row, "capacity_ul");
                scalars[$"{key}_low_threshold_ul"] = Get(row, "low_threshold_ul");
                scalars[$"{key}_full_threshold_ul"] = Get(row, "full_threshold_ul");
                scalars[$"{key}_level_status"] = Get(row, "level_status");
            }
        }

        scalars["work_target_temperature_c"] = TwinSqlite.TableExists(connection, "thermal_point_states")
            ? DeciC(Get(TwinSqlite.One(connection, "SELECT MAX(target_temperature_deci_c) AS v FROM thermal_point_states"), "v"))
            : null;

        scalars["reagent_bottle_capacity_ml"] = TwinSqlite.TableExists(connection, "reagent_bottles")
            ? RoundDiv(Get(TwinSqlite.One(connection, "SELECT MAX(initial_volume_ul) AS v FROM reagent_bottles"), "v"), 1000.0, 1)
            : null;

        return scalars;
    }

    private Dictionary<string, object?> BuildMetrics(SqliteConnection connection)
    {
        var metrics = new Dictionary<string, object?>
        {
            ["total"] = null,
            ["today"] = null,
            ["active"] = null,
        };

        if (!TwinSqlite.TableExists(connection, "staining_tasks"))
        {
            return metrics;
        }

        var total = TwinSqlite.One(connection, "SELECT COUNT(*) AS c FROM staining_tasks");
        metrics["total"] = total is not null ? Get(total, "c") : null;
        var today = TwinSqlite.One(connection, "SELECT COUNT(*) AS c FROM staining_tasks WHERE substr(created_at_utc,1,10)=date('now')");
        metrics["today"] = today is not null ? Get(today, "c") : null;
        var active = TwinSqlite.One(connection, "SELECT COUNT(*) AS c FROM staining_tasks WHERE status NOT IN ('Completed','Cancelled','Failed')");
        metrics["active"] = active is not null ? Get(active, "c") : null;

        return metrics;
    }

    private Dictionary<string, object?> BuildPrecheckResults(SqliteConnection connection)
    {
        var labels = new (string Module, string Label)[]
        {
            ("controller", "主控连接"),
            ("robot-arm", "机械臂回零"),
            ("cooling", "制冷连接"),
            ("sample-scanner", "样本扫码器在线"),
            ("reagent-scanner", "试剂扫码器在线"),
            ("sensors", "液位/传感器读取"),
            ("needle-wash", "洗针准备"),
            ("system-water", "纯水可用"),
            ("pbs", "PBS 可用"),
            ("waste", "废液未满"),
            ("toxic-waste", "排毒桶未满"),
            ("pump", "液位/传感器读取"),
        };
        var labelByModule = labels.ToDictionary(l => l.Module, l => l.Label, StringComparer.Ordinal);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var label in labels.Select(l => l.Label).Distinct())
        {
            result[label] = null;
        }

        if (!TwinSqlite.TableExists(connection, "device_initialization_checks"))
        {
            return result;
        }

        List<Dictionary<string, object?>> rows;
        if (TwinSqlite.TableExists(connection, "device_initialization_runs"))
        {
            rows = TwinSqlite.Many(connection, """
                SELECT c.* FROM device_initialization_checks c
                JOIN device_initialization_runs r ON r.id = c.device_initialization_run_id
                WHERE r.started_at_utc = (SELECT MAX(started_at_utc) FROM device_initialization_runs)
                ORDER BY c.step_no
                """);
        }
        else
        {
            rows = TwinSqlite.Many(connection, "SELECT * FROM device_initialization_checks ORDER BY started_at_utc DESC");
        }

        foreach (var row in rows)
        {
            var moduleCode = Get(row, "module_code")?.ToString();
            if (moduleCode is not null && labelByModule.TryGetValue(moduleCode, out var label))
            {
                var status = Get(row, "status")?.ToString()?.ToLowerInvariant() ?? string.Empty;
                result[label] = status is "succeeded" or "completed" or "success";
            }
        }

        return result;
    }

    private (List<object> Logs, List<object> Warnings) BuildLogs(SqliteConnection connection)
    {
        var logs = new List<object>();
        var warnings = new List<object>();

        if (TwinSqlite.TableExists(connection, "audit_logs"))
        {
            foreach (var row in TwinSqlite.Many(connection, "SELECT created_at_utc, action, message FROM audit_logs ORDER BY created_at_utc DESC LIMIT 30"))
            {
                logs.Add(new Dictionary<string, object?>
                {
                    ["time"] = Get(row, "created_at_utc"),
                    ["type"] = "audit",
                    ["message"] = $"{Str(Get(row, "action"))}: {Str(Get(row, "message"))}",
                });
            }
        }

        if (TwinSqlite.TableExists(connection, "command_receipts"))
        {
            foreach (var row in TwinSqlite.Many(connection, "SELECT created_at_utc, operation, status, error_message FROM command_receipts ORDER BY created_at_utc DESC LIMIT 30"))
            {
                var message = $"{Str(Get(row, "operation"))} -> {Str(Get(row, "status"))}";
                var error = Get(row, "error_message");
                if (!string.IsNullOrEmpty(error?.ToString()))
                {
                    message += $": {Str(error)}";
                    warnings.Add(new Dictionary<string, object?>
                    {
                        ["time"] = Get(row, "created_at_utc"),
                        ["type"] = "command",
                        ["message"] = message,
                    });
                }
                else
                {
                    logs.Add(new Dictionary<string, object?>
                    {
                        ["time"] = Get(row, "created_at_utc"),
                        ["type"] = "command",
                        ["message"] = message,
                    });
                }
            }
        }

        if (TwinSqlite.TableExists(connection, "alarms"))
        {
            foreach (var row in TwinSqlite.Many(connection, "SELECT * FROM alarms ORDER BY created_at_utc DESC LIMIT 30"))
            {
                var message = FirstTruthy(Get(row, "message"), Get(row, "alarm_code")) ?? "alarm";
                warnings.Add(new Dictionary<string, object?>
                {
                    ["time"] = Get(row, "created_at_utc"),
                    ["type"] = "alarm",
                    ["message"] = message,
                });
            }
        }

        return (logs.Take(50).ToList(), warnings.Take(50).ToList());
    }

    private Dictionary<string, object?>? BuildArmPayload(SqliteConnection connection)
    {
        if (!TwinSqlite.TableExists(connection, "robot_arm_states"))
        {
            return null;
        }

        var arm = TwinSqlite.One(connection, "SELECT * FROM robot_arm_states LIMIT 1");
        if (arm is null)
        {
            return null;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        var x = Get(arm, "current_x_um");
        if (x is not null)
        {
            payload["x"] = RoundDiv(x, 1000.0, 3);
        }

        var y = Get(arm, "current_y_um");
        if (y is not null)
        {
            payload["y"] = RoundDiv(y, 1000.0, 3);
        }

        var z = Get(arm, "current_z_um");
        if (z is not null)
        {
            var zMilli = RoundDiv(z, 1000.0, 3);
            payload["z1"] = zMilli;
            payload["z2"] = zMilli;
        }

        return payload.Count > 0 ? payload : null;
    }

    private Dictionary<string, object?> BuildCameras(SqliteConnection connection)
    {
        var cameras = new Dictionary<string, object?>
        {
            ["reagent"] = null,
            ["arm"] = null,
        };

        if (TwinSqlite.TableExists(connection, "reagent_scan_sessions"))
        {
            var row = TwinSqlite.One(connection, "SELECT status FROM reagent_scan_sessions ORDER BY started_at_utc DESC LIMIT 1");
            cameras["reagent"] = row is not null ? StatusToFrontend(Get(row, "status")) : null;
        }

        if (TwinSqlite.TableExists(connection, "sample_scan_sessions"))
        {
            var row = TwinSqlite.One(connection, "SELECT status FROM sample_scan_sessions ORDER BY started_at_utc DESC LIMIT 1");
            cameras["arm"] = row is not null ? StatusToFrontend(Get(row, "status")) : null;
        }

        return cameras;
    }

    private List<object> BuildWorkflowProfiles(SqliteConnection connection)
    {
        var profiles = new List<object>();
        if (!(TwinSqlite.TableExists(connection, "workflow_versions")
              && TwinSqlite.TableExists(connection, "workflow_definitions")
              && TwinSqlite.TableExists(connection, "workflow_steps")))
        {
            return profiles;
        }

        var versions = TwinSqlite.Many(connection, """
            SELECT v.*, d.name AS definition_name, d.workflow_type, d.description, d.code AS definition_code
            FROM workflow_versions v JOIN workflow_definitions d ON d.id = v.workflow_definition_id
            ORDER BY v.status='Published' DESC, v.version_no DESC
            """);

        foreach (var version in versions)
        {
            var steps = TwinSqlite.Many(connection, "SELECT * FROM workflow_steps WHERE workflow_version_id = @p0 ORDER BY step_no", Get(version, "id"));
            var stepList = new List<object>();
            foreach (var step in steps)
            {
                var opKeyRaw = (Get(step, "action_type")?.ToString() ?? string.Empty).ToLowerInvariant();
                stepList.Add(new Dictionary<string, object?>
                {
                    ["id"] = Get(step, "id"),
                    ["label"] = OrFallback(Get(step, "step_name"), Get(step, "major_step_code"), Get(step, "action_type")),
                    ["opKey"] = string.IsNullOrEmpty(opKeyRaw) ? "custom" : opKeyRaw,
                    ["durationSec"] = Get(step, "duration_seconds"),
                    ["toleranceSec"] = 0,
                    ["immediateAfterPrev"] = false,
                    ["requiresTemp"] = Get(step, "target_temperature_deci_c") is not null,
                    ["targetTempC"] = DeciC(Get(step, "target_temperature_deci_c")),
                    ["reagentRole"] = Get(step, "reagent_code")?.ToString() ?? string.Empty,
                    ["notes"] = Get(step, "failure_strategy")?.ToString() ?? string.Empty,
                });
            }

            profiles.Add(new Dictionary<string, object?>
            {
                ["id"] = Get(version, "id"),
                ["name"] = Get(version, "definition_name"),
                ["stainType"] = Get(version, "workflow_type"),
                ["version"] = OrFallback(Get(version, "version_label"), Get(version, "version_no"), string.Empty)?.ToString() ?? string.Empty,
                ["description"] = OrFallback(Get(version, "description"), Get(version, "change_note"), string.Empty)?.ToString() ?? string.Empty,
                ["steps"] = stepList,
            });
        }

        return profiles;
    }

    private Dictionary<string, object?> BuildControlValues(Dictionary<string, object?> scalars)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in Registry.Value)
        {
            if (!string.IsNullOrEmpty(entry.ControlId))
            {
                values[entry.ControlId!] = null;
            }
        }

        values["reagentTempText"] = Get(scalars, "reagent_current_temperature_c");
        values["settingsReagentTargetInput"] = Get(scalars, "reagent_target_temperature_c");
        values["reagentCoolingCurrentInput"] = Get(scalars, "reagent_current_temperature_c");
        values["reagentCoolingTargetInput"] = Get(scalars, "reagent_target_temperature_c");
        values["settingsPureThresholdInput"] = Pct(Get(scalars, "pure_low_threshold_ul"), Get(scalars, "pure_capacity_ul"));
        values["settingsPbsThresholdInput"] = Pct(Get(scalars, "pbs_low_threshold_ul"), Get(scalars, "pbs_capacity_ul"));
        values["settingsWasteThresholdInput"] = Pct(Get(scalars, "waste_full_threshold_ul"), Get(scalars, "waste_capacity_ul"));
        values["settingsToxicThresholdInput"] = Pct(Get(scalars, "toxic_full_threshold_ul"), Get(scalars, "toxic_capacity_ul"));
        values["settingsWorkTargetInput"] = Get(scalars, "work_target_temperature_c");
        values["settingsReagentCapacityInput"] = Get(scalars, "reagent_bottle_capacity_ml");
        values["settingsNeedleGapInput"] = null;

        return values;
    }

    // ---- 转换辅助：对应 snapshot.py 中的同名辅助函数 ----

    private static object? Get(Dictionary<string, object?>? row, string key)
        => row is not null && row.TryGetValue(key, out var value) ? value : null;

    private static string? Str(object? value) => value is null ? "None" : value.ToString();

    private static bool IsNumber(object? value) => value is long or int or short or byte or float or double or decimal;

    private static long? Int(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is long l)
        {
            return l;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is double d)
        {
            return (long)d;
        }

        if (value is decimal m)
        {
            return (long)m;
        }

        if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            return (long)floating;
        }

        return null;
    }

    private static double? Dbl(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is double d)
        {
            return d;
        }

        if (value is long l)
        {
            return l;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is decimal m)
        {
            return (double)m;
        }

        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? RoundDiv(object? value, double divisor, int digits)
    {
        var d = Dbl(value);
        return d.HasValue ? Math.Round(d.Value / divisor, digits) : null;
    }

    private static double? DeciC(object? value) => RoundDiv(value, 10.0, 1);

    private static double? Pct(object? current, object? capacity)
    {
        var current_value = Dbl(current);
        if (current_value is null)
        {
            return null;
        }

        var capacity_value = Dbl(capacity);
        if (capacity_value is null || capacity_value == 0)
        {
            return null;
        }

        return Math.Round(current_value.Value / capacity_value.Value * 100.0, 1);
    }

    private static string? StatusToFrontend(object? status)
    {
        if (status is null)
        {
            return null;
        }

        var lowered = status.ToString()!.ToLowerInvariant();
        if (StatusComplete.Contains(lowered))
        {
            return "complete";
        }

        if (StatusRunning.Contains(lowered))
        {
            return "running";
        }

        if (StatusError.Contains(lowered))
        {
            return "error";
        }

        if (StatusIdle.Contains(lowered))
        {
            return "idle";
        }

        return status.ToString();
    }

    private static string? ReagentFrontendName(object? positionNo, object? columnNo, object? rowNo)
    {
        var column = Int(columnNo);
        var row = Int(rowNo);
        if (column is not null && column != 0 && row is not null && row != 0)
        {
            return $"试剂_S{column}{row}";
        }

        var position = Int(positionNo);
        if (position is not null && position != 0)
        {
            var pos = position.Value;
            var col = (pos - 1) / 8 + 1;
            var r = (pos - 1) % 8 + 1;
            return $"试剂_S{col}{r}";
        }

        return null;
    }

    private static string? SlideFrontendName(object? drawerCode, object? slotNo)
    {
        var code = drawerCode?.ToString();
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var slot = Int(slotNo);
        if (slot is null)
        {
            return null;
        }

        if (!DrawerToChannel.TryGetValue(code.ToUpperInvariant(), out var channel))
        {
            return null;
        }

        return $"玻片_R{channel}{slot}";
    }

    private static string NormalizeSlideFrontendName(string name)
    {
        var marker = name.LastIndexOf("_R", StringComparison.Ordinal);
        return marker >= 0 ? name[(marker + 1)..] : name;
    }

    private static string? MixFrontendName(object? positionNo)
    {
        var position = Int(positionNo);
        if (position is null)
        {
            return null;
        }

        var pos = position.Value;
        var row = (pos - 1) / 2 + 1;
        var col = (pos - 1) % 2 + 1;
        return $"配液_R{row}_C{col}";
    }

    private static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string s)
        {
            return !string.IsNullOrEmpty(s);
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is long or int or short or byte or float or double or decimal)
        {
            return Dbl(value) != 0;
        }

        return true;
    }

    // 对应 Python `a or b or c ...`：返回第一个真值，否则返回最后一个值本身。
    private static object? OrFallback(params object?[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        for (var i = 0; i < values.Length - 1; i++)
        {
            if (IsTruthy(values[i]))
            {
                return values[i];
            }
        }

        return values[^1];
    }

    private static string? FirstTruthy(params object?[] values)
    {
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            var text = value.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return null;
    }

    private sealed record RegistryEntry(string? Category, string? Name, string? ControlId);

    // ---- 嵌入资产加载 ----

    private static IReadOnlyList<RegistryEntry> LoadRegistry()
    {
        using var stream = OpenResource("frontend_registry.json");
        using var document = JsonDocument.Parse(stream);
        var list = new List<RegistryEntry>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            list.Add(new RegistryEntry(StringProperty(element, "category"), StringProperty(element, "name"), StringProperty(element, "controlId")));
        }

        return list;

        static string? StringProperty(JsonElement element, string name)
            => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static IReadOnlyList<Dictionary<string, string>> LoadMappingRows()
    {
        string text;
        using (var stream = OpenResource("frontend_db_mapping.csv"))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var rows = new List<Dictionary<string, string>>();
        using var stringReader = new StringReader(text);
        using var parser = new TextFieldParser(stringReader)
        {
            TextFieldType = FieldType.Delimited,
        };
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        string[]? header = null;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (header is null)
            {
                header = fields;
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Length; i++)
            {
                row[header[i]] = i < fields.Length ? fields[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static byte[] LoadMappingCsvBytes()
    {
        using var stream = OpenResource("frontend_db_mapping.csv");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Stream OpenResource(string fileName)
    {
        var assembly = typeof(TwinSnapshotService).Assembly;
        var fullName = assembly.GetManifestResourceNames().First(name => name.EndsWith(fileName, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(fullName) ?? throw new InvalidOperationException($"Embedded twin resource not found: {fileName}");
    }
}
