using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DigitalTwinCoordinateImportService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    public const string DefaultVersionLabel = "DigitalTwinXY-v1";
    public const string DefaultMappingVersion = "DigitalTwinXY-v1";

    private const string ImportKind = "DigitalTwinCoordinateImport";
    private const string ReferenceOnlyPointType = "ReferenceOnly";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredHeaders =
    [
        "序号",
        "类别",
        "名称",
        "行",
        "列",
        "x_mm",
        "y_mm",
        "形状",
        "半径_mm",
        "宽度_mm",
        "高度_mm",
        "备注"
    ];

    public async Task<DigitalTwinCoordinateImportResponse> PreviewAsync(
        PreviewDigitalTwinCoordinateImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await BuildPreviewAsync(
            request.SourceFilePath,
            NormalizeOptional(request.ProfileCode) ?? ReferenceDataSeeder.DefaultCoordinateProfileCode,
            NormalizeOptional(request.VersionLabel) ?? DefaultVersionLabel,
            NormalizeOptional(request.MappingVersion) ?? DefaultMappingVersion,
            cancellationToken);

        return preview.ToResponse(
            dryRun: true,
            commandId: string.Empty,
            replayed: false,
            existingVersionReused: false,
            versionId: null,
            importedAtUtc: null,
            importedBy: null,
            message: preview.Errors.Count == 0
                ? "Digital twin coordinate import preview completed."
                : "Digital twin coordinate import preview found blocking mapping errors.");
    }

    public Task<DigitalTwinCoordinateImportResponse> ApplyAsync(
        ApplyDigitalTwinCoordinateImportRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "engineering.coordinate.digital_twin.import",
            request,
            actor,
            async () =>
            {
                RequireEngineer(actor);
                var reason = RequireValue(request.Reason, "reason");
                var profileCode = NormalizeOptional(request.ProfileCode) ?? ReferenceDataSeeder.DefaultCoordinateProfileCode;
                var versionLabel = NormalizeOptional(request.VersionLabel) ?? DefaultVersionLabel;
                var mappingVersion = NormalizeOptional(request.MappingVersion) ?? DefaultMappingVersion;
                var preview = await BuildPreviewAsync(request.SourceFilePath, profileCode, versionLabel, mappingVersion, cancellationToken);
                if (preview.Errors.Count > 0)
                {
                    throw new BusinessRuleException(
                        "digital_twin_coordinate_import_invalid",
                        string.Join("; ", preview.Errors),
                        StatusCodes.Status409Conflict);
                }

                var profile = await dbContext.CoordinateProfiles
                    .Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Code == profileCode, cancellationToken)
                    ?? throw new BusinessRuleException(
                        "coordinate_profile_not_found",
                        $"Coordinate profile {profileCode} was not found.",
                        StatusCodes.Status404NotFound);

                var existing = profile.Versions.SingleOrDefault(x => x.VersionLabel == versionLabel);
                if (existing is not null)
                {
                    if (!IsSameImport(existing, preview.SourceFileHashSha256, mappingVersion))
                    {
                        throw new BusinessRuleException(
                            "digital_twin_coordinate_version_conflict",
                            $"Coordinate version label {versionLabel} already exists and was not created from the same CSV hash and mapping version.",
                            StatusCodes.Status409Conflict);
                    }

                    var replayResponse = preview.ToResponse(
                        dryRun: false,
                        commandId: request.CommandId,
                        replayed: false,
                        existingVersionReused: true,
                        versionId: existing.Id,
                        importedAtUtc: existing.CreatedAtUtc,
                        importedBy: actor.Username,
                        message: "Digital twin coordinate version already exists for this CSV hash; no duplicate version or points were created.");
                    AddAudit(actor, "coordinate.digital_twin_import.reuse", "CoordinateProfileVersion", existing.Id, new
                    {
                        profileCode,
                        versionLabel,
                        mappingVersion,
                        preview.SourceFileHashSha256,
                        reason
                    });
                    return new CommandExecutionResult<DigitalTwinCoordinateImportResponse>(replayResponse, "CoordinateProfileVersion", existing.Id);
                }

                var sourceVersion = await dbContext.CoordinateProfileVersions
                    .AsNoTracking()
                    .Where(x => x.CoordinateProfileId == profile.Id && x.IsActive)
                    .SingleOrDefaultAsync(cancellationToken);

                var versionNo = profile.Versions.Count == 0 ? 1 : profile.Versions.Max(x => x.VersionNo) + 1;
                var now = DateTimeOffset.UtcNow;
                var version = new CoordinateProfileVersion
                {
                    CoordinateProfile = profile,
                    VersionNo = versionNo,
                    VersionLabel = versionLabel,
                    Status = CoordinateProfileVersionStatus.Draft,
                    IsActive = false,
                    UsageScope = CoordinateVersionUsageScope.MockOnly,
                    VerificationStatus = CoordinateVersionVerificationStatus.Unverified,
                    SourceVersionId = sourceVersion?.Id,
                    ChangeReason = reason,
                    ChangeSummaryJson = BuildChangeSummaryJson(preview, actor, now, reason),
                    ValidationResultJson = BuildValidationResultJson(preview),
                    CreatedByUserId = actor.UserId,
                    CreatedAtUtc = now
                };
                dbContext.CoordinateProfileVersions.Add(version);

                foreach (var row in preview.Rows)
                {
                    if (string.IsNullOrWhiteSpace(row.TargetPointCode))
                    {
                        continue;
                    }

                    version.TargetPoints.Add(new CoordinatePoint
                    {
                        CoordinateProfileId = profile.Id,
                        CoordinateProfileVersion = version,
                        PointCode = row.TargetPointCode,
                        PointType = row.PointType ?? ReferenceOnlyPointType,
                        PresetXUm = row.MachineXUm,
                        PresetYUm = row.MachineYUm,
                        CalibratedXUm = row.MachineXUm,
                        CalibratedYUm = row.MachineYUm,
                        CalibratedZUm = null,
                        SafeZUm = null,
                        LiquidDetectZUm = null,
                        DispenseZUm = null,
                        ActionOffsetXUm = null,
                        ActionOffsetYUm = null,
                        ActionOffsetZUm = null,
                        RequiresCalibration = row.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget,
                        ValidationStatus = row.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget
                            ? CoordinateTargetPointValidationStatus.NeedsCalibration
                            : CoordinateTargetPointValidationStatus.Unverified,
                        ValidationMessage = row.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget
                            ? "DigitalTwinXY XY imported; Z height, safety parameters, motion limits, and engineering validation are pending."
                            : row.Reason,
                        IsEnabled = row.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget,
                        CreatedAtUtc = now
                    });
                }

                AddAudit(actor, "coordinate.digital_twin_import.apply", "CoordinateProfileVersion", version.Id, new
                {
                    profileCode,
                    versionLabel,
                    mappingVersion,
                    preview.SourceFileName,
                    preview.SourceFileHashSha256,
                    totalRows = preview.Rows.Count,
                    executableTargets = preview.ExecutableTargetCount,
                    referenceOnly = preview.ReferenceOnlyCount,
                    reason
                });

                var response = preview.ToResponse(
                    dryRun: false,
                    commandId: request.CommandId,
                    replayed: false,
                    existingVersionReused: false,
                    versionId: version.Id,
                    importedAtUtc: now,
                    importedBy: actor.Username,
                    message: "Digital twin coordinate version created as Draft / pending engineering calibration.");
                return new CommandExecutionResult<DigitalTwinCoordinateImportResponse>(response, "CoordinateProfileVersion", version.Id);
            },
            cancellationToken);
    }

    private async Task<DigitalTwinCoordinatePreview> BuildPreviewAsync(
        string sourceFilePath,
        string profileCode,
        string versionLabel,
        string mappingVersion,
        CancellationToken cancellationToken)
    {
        var path = RequireValue(sourceFilePath, "sourceFilePath");
        if (!File.Exists(path))
        {
            throw new BusinessRuleException("digital_twin_csv_not_found", $"CSV file was not found: {path}", StatusCodes.Status404NotFound);
        }

        var sourceFileName = Path.GetFileName(path);
        var hash = await ComputeSha256Async(path, cancellationToken);
        var parsed = await ParseCsvAsync(path, cancellationToken);
        var rows = new List<DigitalTwinCoordinateRowDispositionResponse>();
        var errors = new List<string>(parsed.Errors);

        foreach (var csvRow in parsed.Rows)
        {
            rows.Add(MapRow(csvRow));
        }

        ValidateMappedRows(rows, errors);

        return new DigitalTwinCoordinatePreview(
            profileCode,
            versionLabel,
            mappingVersion,
            sourceFileName,
            hash,
            rows,
            errors);
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapRow(CsvCoordinateRow row)
    {
        if (row.ParseError is not null)
        {
            return row.ToDisposition(
                DigitalTwinCoordinateDisposition.Rejected,
                null,
                null,
                null,
                row.ParseError);
        }

        return row.Category switch
        {
            "试剂区" => MapReagent(row),
            "玻片通道" => MapSlide(row),
            "混合液体配液区" => MapDabMixHole(row),
            "A/B液" => MapDabSource(row),
            "洗针头" => MapWash(row),
            "机械臂相对位置" => MapRobotReference(row),
            "混匀电机" => MapMixerReference(row),
            "相机" => row.ToDisposition(DigitalTwinCoordinateDisposition.ReferenceOnly, CameraCode(row.Name), ReferenceOnlyPointType, "Camera references are not automatically added to robot paths."),
            "废液孔" => row.ToDisposition(DigitalTwinCoordinateDisposition.ReferenceOnly, ReferenceCode("WasteHole", row.RowNumber, row.ColumnNo), ReferenceOnlyPointType, "Waste holes are physical references only until robot execution is explicitly validated."),
            "排毒孔" => row.ToDisposition(DigitalTwinCoordinateDisposition.ReferenceOnly, ReferenceCode("ToxicHole", row.RowNumber, row.ColumnNo), ReferenceOnlyPointType, "Detox holes are physical references only until robot execution is explicitly validated."),
            "清洗孔" => row.ToDisposition(DigitalTwinCoordinateDisposition.ReferenceOnly, ReferenceCode("CleanHole", row.RowNumber, row.ColumnNo), ReferenceOnlyPointType, "Cleaning holes are physical references only until robot execution is explicitly validated."),
            _ => row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, $"Unsupported category: {row.Category}")
        };
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapReagent(CsvCoordinateRow row)
    {
        if (row.RowNo is not >= 1 or > 8 || row.ColumnNo is not >= 1 or > 5)
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "Reagent row requires 行=1..8 and 列=1..5.");
        }

        var positionNo = ((row.ColumnNo.Value - 1) * 8) + row.RowNo.Value;
        var logicalLabel = $"S{row.ColumnNo.Value}{row.RowNo.Value}";
        return row.ToDisposition(
            DigitalTwinCoordinateDisposition.ExecutableTarget,
            $"R{positionNo}",
            "ReagentRackPosition",
            logicalLabel,
            $"{logicalLabel} maps to R{positionNo}.");
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapSlide(CsvCoordinateRow row)
    {
        if (!TryParseTwoDigitName(row.Name, "R", out var channel, out var slotFromTop)
            || channel is < 1 or > 4
            || slotFromTop is < 1 or > 4)
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "Slide channel name must be R11..R44.");
        }

        var drawer = "ABCD"[channel - 1];
        var slotNo = 5 - slotFromTop;
        var target = $"{drawer}-{slotNo:00}";
        return row.ToDisposition(
            DigitalTwinCoordinateDisposition.ExecutableTarget,
            target,
            "PhysicalSlot",
            row.Name,
            $"{row.Name} maps to {target}.");
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapDabMixHole(CsvCoordinateRow row)
    {
        if (row.RowNo is not >= 1 or > 4 || row.ColumnNo is not >= 1 or > 2)
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "DAB mix hole requires 行=1..4 and 列=1..2.");
        }

        var logical = $"P{row.RowNo.Value}{row.ColumnNo.Value}";
        var positionNo = ((row.RowNo.Value - 1) * 2) + row.ColumnNo.Value;
        return row.ToDisposition(
            DigitalTwinCoordinateDisposition.ExecutableTarget,
            $"M{positionNo}",
            "DabMixPosition",
            logical,
            $"{logical} maps to DAB M{positionNo}.");
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapDabSource(CsvCoordinateRow row)
    {
        if (row.Name.Contains("A", StringComparison.OrdinalIgnoreCase))
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.ExecutableTarget, "DabA", "DabSourceBottle", "DAB-A", "DAB A liquid is the upper source.");
        }

        if (row.Name.Contains("B", StringComparison.OrdinalIgnoreCase))
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.ExecutableTarget, "DabB", "DabSourceBottle", "DAB-B", "DAB B liquid is the lower source.");
        }

        return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "DAB source name must identify A or B liquid.");
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapWash(CsvCoordinateRow row)
    {
        if (row.RowNo is not >= 1 or > 2 || row.ColumnNo is not >= 1 or > 2)
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "Wash head requires 行=1..2 and 列=1..2.");
        }

        var target = (row.RowNo.Value, row.ColumnNo.Value) switch
        {
            (1, 1) => "WashOuterLeft",
            (1, 2) => "WashInnerLeft",
            (2, 1) => "WashOuterRight",
            (2, 2) => "WashInnerRight",
            _ => null
        };
        return target is null
            ? row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "Wash mapping is ambiguous.")
            : row.ToDisposition(
                DigitalTwinCoordinateDisposition.ExecutableTarget,
                target,
                "WashPosition",
                row.RowNo.Value == 1 ? "Needle1Wash" : "Needle2Wash",
                "Left column is outer wash, right column is inner wash; upper group is Needle1 and lower group is Needle2.");
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapRobotReference(CsvCoordinateRow row)
    {
        var code = row.Name switch
        {
            "针头_Z1" => "Needle1",
            "针头_Z2" => "Needle2",
            "机械臂随动相机" => "ArmCamera",
            _ => ReferenceCode("RobotReference", row.RowNo, row.ColumnNo)
        };
        var reason = code switch
        {
            "Needle1" => "Needle1/Z1 is the machine origin (0,0).",
            "Needle2" => "Needle2/Z2 is the confirmed offset (0,+25000 um).",
            "ArmCamera" => "Arm camera is a reference point until robot execution is explicitly validated.",
            _ => "Robot-relative physical reference."
        };
        return row.ToDisposition(DigitalTwinCoordinateDisposition.ReferenceOnly, code, code.StartsWith("Needle", StringComparison.Ordinal) ? "Needle" : ReferenceOnlyPointType, code, reason);
    }

    private static DigitalTwinCoordinateRowDispositionResponse MapMixerReference(CsvCoordinateRow row)
    {
        if (!TryParseTrailingNumber(row.Name, out var mixerNo) || mixerNo is < 1 or > 4)
        {
            return row.ToDisposition(DigitalTwinCoordinateDisposition.Rejected, null, null, null, "Mixer module name must resolve to Mixer1..Mixer4.");
        }

        return row.ToDisposition(
            DigitalTwinCoordinateDisposition.ReferenceOnly,
            $"Mixer{mixerNo}",
            ReferenceOnlyPointType,
            $"Mixer{mixerNo}",
            "Mixer module reference is not the same as DAB M1-M8 and is not automatically added to robot paths.");
    }

    private static void ValidateMappedRows(
        IReadOnlyList<DigitalTwinCoordinateRowDispositionResponse> rows,
        List<string> errors)
    {
        errors.AddRange(rows
            .Where(x => x.Disposition == DigitalTwinCoordinateDisposition.Rejected)
            .Select(x => $"CSV row {x.RowNumber} ({x.Category}/{x.CsvName}): {x.Reason}"));

        var duplicateTargets = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetPointCode))
            .GroupBy(x => x.TargetPointCode!, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => $"{x.Key} from rows {string.Join(", ", x.Select(r => r.RowNumber))}")
            .ToList();
        if (duplicateTargets.Count > 0)
        {
            errors.Add($"Duplicate coordinate point mappings: {string.Join("; ", duplicateTargets)}.");
        }

        RequireExecutableSet(rows, Enumerable.Range(1, 40).Select(x => $"R{x}"), "reagent R1-R40", errors);
        RequireExecutableSet(rows, "ABCD".SelectMany(drawer => Enumerable.Range(1, 4).Select(slot => $"{drawer}-{slot:00}")), "slide A-01-D-04", errors);
        RequireExecutableSet(rows, Enumerable.Range(1, 8).Select(x => $"M{x}"), "DAB M1-M8", errors);
        RequireExecutableSet(rows, ["DabA", "DabB"], "DAB A/B sources", errors);
        RequireExecutableSet(rows, ["WashOuterLeft", "WashInnerLeft", "WashOuterRight", "WashInnerRight"], "wash positions", errors);
        RequireReferenceSet(rows, ["Needle1", "Needle2"], "Needle1/Needle2 references", errors);

        var needle1 = rows.SingleOrDefault(x => x.TargetPointCode == "Needle1");
        if (needle1 is null || needle1.MachineXUm != 0 || needle1.MachineYUm != 0)
        {
            errors.Add("Needle1/Z1 must be exactly (0,0) um.");
        }

        var needle2 = rows.SingleOrDefault(x => x.TargetPointCode == "Needle2");
        if (needle2 is null || needle2.MachineXUm != 0 || needle2.MachineYUm != 25_000)
        {
            errors.Add("Needle2/Z2 must be exactly (0,+25000) um.");
        }
    }

    private static void RequireExecutableSet(
        IReadOnlyList<DigitalTwinCoordinateRowDispositionResponse> rows,
        IEnumerable<string> requiredCodes,
        string label,
        List<string> errors)
    {
        var mapped = rows
            .Where(x => x.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget)
            .Select(x => x.TargetPointCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredCodes.Where(x => !mapped.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"Missing executable {label} mappings: {string.Join(", ", missing)}.");
        }
    }

    private static void RequireReferenceSet(
        IReadOnlyList<DigitalTwinCoordinateRowDispositionResponse> rows,
        IEnumerable<string> requiredCodes,
        string label,
        List<string> errors)
    {
        var mapped = rows
            .Where(x => x.Disposition == DigitalTwinCoordinateDisposition.ReferenceOnly)
            .Select(x => x.TargetPointCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredCodes.Where(x => !mapped.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"Missing reference {label}: {string.Join(", ", missing)}.");
        }
    }

    private static async Task<ParsedCoordinateCsv> ParseCsvAsync(string path, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
        if (lines.Length == 0)
        {
            return new ParsedCoordinateCsv([], ["CSV file is empty."]);
        }

        var headers = ParseCsvLine(lines[0]).Select(x => x.Trim()).ToArray();
        var headerLookup = headers
            .Select((value, index) => new { value, index })
            .GroupBy(x => x.value, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(v => v.index).ToArray(), StringComparer.Ordinal);
        foreach (var required in RequiredHeaders)
        {
            if (!headerLookup.ContainsKey(required))
            {
                errors.Add($"CSV header is missing required column: {required}.");
            }
        }

        foreach (var duplicate in headerLookup.Where(x => x.Value.Length > 1))
        {
            errors.Add($"CSV header contains duplicate column: {duplicate.Key}.");
        }

        if (errors.Count > 0)
        {
            return new ParsedCoordinateCsv([], errors);
        }

        var rows = new List<CsvCoordinateRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = ParseCsvLine(lines[i]);
            if (fields.Count != headers.Length)
            {
                rows.Add(CsvCoordinateRow.FromParseError(
                    i + 1,
                    fields.ElementAtOrDefault(headerLookup["类别"][0]) ?? string.Empty,
                    fields.ElementAtOrDefault(headerLookup["名称"][0]) ?? string.Empty,
                    "CSV field count does not match header count."));
                continue;
            }

            string value(string header) => fields[headerLookup[header][0]].Trim();
            rows.Add(CsvCoordinateRow.FromFields(
                lineNumber: i + 1,
                sequenceText: value("序号"),
                category: value("类别"),
                name: value("名称"),
                rowText: value("行"),
                columnText: value("列"),
                xText: value("x_mm"),
                yText: value("y_mm")));
        }

        return new ParsedCoordinateCsv(rows, errors);
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildChangeSummaryJson(
        DigitalTwinCoordinatePreview preview,
        AuthenticatedUser actor,
        DateTimeOffset importedAtUtc,
        string reason)
    {
        return JsonSerializer.Serialize(new
        {
            importKind = ImportKind,
            source = new
            {
                fileName = preview.SourceFileName,
                sha256 = preview.SourceFileHashSha256,
                importedAtUtc,
                importedByUserId = actor.UserId,
                importedBy = actor.Username,
                mappingVersion = preview.MappingVersion
            },
            rules = new
            {
                coordinateSystem = "Needle1/Z1=(0,0); MachineX_um=CsvX_mm*1000; MachineY_um=CsvY_mm*1000",
                direction = "X positive left; Y positive down",
                needle2OffsetUm = new { x = 0, y = 25000 },
                noTransform = true
            },
            reason,
            summary = new
            {
                totalRows = preview.Rows.Count,
                executableTargets = preview.ExecutableTargetCount,
                referenceOnly = preview.ReferenceOnlyCount,
                rejected = preview.RejectedCount
            },
            rowDispositions = preview.Rows.Select(x => new
            {
                row = x.RowNumber,
                category = x.Category,
                csvName = x.CsvName,
                label = x.CsvLogicalLabel,
                disposition = x.Disposition,
                target = x.TargetPointCode,
                type = x.PointType,
                xUm = x.MachineXUm,
                yUm = x.MachineYUm,
                x.Reason
            })
        }, JsonOptions);
    }

    private static string BuildValidationResultJson(DigitalTwinCoordinatePreview preview)
    {
        return JsonSerializer.Serialize(new
        {
            status = "PendingEngineeringCalibration",
            importKind = ImportKind,
            mappingVersion = preview.MappingVersion,
            xyImported = true,
            requiredHeightsComplete = false,
            calibrationVerified = false,
            safetyParametersComplete = false,
            speedLimitsConfigured = false,
            accelerationLimitsConfigured = false,
            softLimitsConfigured = false,
            realReady = false,
            missingConfiguration = preview.PendingConfiguration
        }, JsonOptions);
    }

    private static bool IsSameImport(CoordinateProfileVersion version, string sourceFileHashSha256, string mappingVersion)
    {
        try
        {
            using var json = JsonDocument.Parse(version.ChangeSummaryJson);
            var root = json.RootElement;
            if (!root.TryGetProperty("importKind", out var importKind)
                || !string.Equals(importKind.GetString(), ImportKind, StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("source", out var source))
            {
                return false;
            }

            return source.TryGetProperty("sha256", out var hash)
                && source.TryGetProperty("mappingVersion", out var mapping)
                && string.Equals(hash.GetString(), sourceFileHashSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(mapping.GetString(), mappingVersion, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseTwoDigitName(string value, string prefix, out int first, out int second)
    {
        first = 0;
        second = 0;
        var normalized = value.Trim();
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || normalized.Length < prefix.Length + 2)
        {
            return false;
        }

        return int.TryParse(normalized[prefix.Length].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out first)
            && int.TryParse(normalized[(prefix.Length + 1)..(prefix.Length + 2)], NumberStyles.None, CultureInfo.InvariantCulture, out second);
    }

    private static bool TryParseTrailingNumber(string value, out int number)
    {
        number = 0;
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string ReferenceCode(string prefix, int? row, int? column)
    {
        return row is null || column is null ? prefix : $"{prefix}_R{row}_C{column}";
    }

    private static string CameraCode(string name)
    {
        if (name.Contains("试剂", StringComparison.Ordinal))
        {
            return "ReagentScannerCamera";
        }

        return name.Contains("随动", StringComparison.Ordinal) ? "ArmCamera" : "CameraReference";
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityType, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static void RequireEngineer(AuthenticatedUser actor)
    {
        if (!actor.HasRole("engineer") && !actor.HasRole("admin"))
        {
            throw new BusinessRuleException("coordinate_permission_denied", "Coordinate changes require engineer or admin role.", StatusCodes.Status403Forbidden);
        }
    }

    private static string RequireValue(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException($"{fieldName}_required", $"{fieldName} is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record ParsedCoordinateCsv(
        IReadOnlyList<CsvCoordinateRow> Rows,
        IReadOnlyList<string> Errors);

    private sealed record DigitalTwinCoordinatePreview(
        string ProfileCode,
        string VersionLabel,
        string MappingVersion,
        string SourceFileName,
        string SourceFileHashSha256,
        IReadOnlyList<DigitalTwinCoordinateRowDispositionResponse> Rows,
        IReadOnlyList<string> Errors)
    {
        public int ExecutableTargetCount => Rows.Count(x => x.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget);
        public int ReferenceOnlyCount => Rows.Count(x => x.Disposition == DigitalTwinCoordinateDisposition.ReferenceOnly);
        public int RejectedCount => Rows.Count(x => x.Disposition == DigitalTwinCoordinateDisposition.Rejected);

        public IReadOnlyList<string> PendingConfiguration =>
            Rows
                .Where(x => x.Disposition == DigitalTwinCoordinateDisposition.ExecutableTarget)
                .Select(x => $"{x.TargetPointCode}: Z height, safe height, approach/leave height, liquid detect/aspirate height, dispense height, action offsets, speed/acceleration limits, soft limits, and engineering validation are pending.")
                .ToList();

        public DigitalTwinCoordinateImportResponse ToResponse(
            bool dryRun,
            string commandId,
            bool replayed,
            bool existingVersionReused,
            string? versionId,
            DateTimeOffset? importedAtUtc,
            string? importedBy,
            string message)
        {
            return new DigitalTwinCoordinateImportResponse(
                Errors.Count == 0,
                dryRun,
                commandId,
                replayed,
                existingVersionReused,
                ProfileCode,
                VersionLabel,
                MappingVersion,
                SourceFileName,
                SourceFileHashSha256,
                importedAtUtc,
                importedBy,
                versionId,
                Rows.Count,
                ExecutableTargetCount,
                ReferenceOnlyCount,
                RejectedCount,
                Rows,
                Errors,
                PendingConfiguration,
                message);
        }
    }

    private sealed record CsvCoordinateRow(
        int LineNumber,
        int RowNumber,
        string Category,
        string Name,
        int? RowNo,
        int? ColumnNo,
        long? MachineXUm,
        long? MachineYUm,
        string? ParseError)
    {
        public static CsvCoordinateRow FromFields(
            int lineNumber,
            string sequenceText,
            string category,
            string name,
            string rowText,
            string columnText,
            string xText,
            string yText)
        {
            var errors = new List<string>();
            var rowNumber = TryParseInt(sequenceText, out var parsedSequence)
                ? parsedSequence
                : lineNumber;
            if (string.IsNullOrWhiteSpace(category))
            {
                errors.Add("类别 is required");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("名称 is required");
            }

            var rowNo = ParseNullableInt(rowText, "行", errors);
            var columnNo = ParseNullableInt(columnText, "列", errors);
            var xUm = ParseMicrometers(xText, "x_mm", errors);
            var yUm = ParseMicrometers(yText, "y_mm", errors);
            return new CsvCoordinateRow(
                lineNumber,
                rowNumber,
                category.Trim(),
                name.Trim(),
                rowNo,
                columnNo,
                xUm,
                yUm,
                errors.Count == 0 ? null : string.Join("; ", errors));
        }

        public static CsvCoordinateRow FromParseError(int lineNumber, string category, string name, string error) =>
            new(lineNumber, lineNumber, category.Trim(), name.Trim(), null, null, null, null, error);

        public DigitalTwinCoordinateRowDispositionResponse ToDisposition(
            string disposition,
            string? targetPointCode,
            string? pointType,
            string? csvLogicalLabel,
            string reason)
        {
            return new DigitalTwinCoordinateRowDispositionResponse(
                RowNumber,
                Category,
                Name,
                csvLogicalLabel,
                disposition,
                targetPointCode,
                pointType,
                MachineXUm,
                MachineYUm,
                reason);
        }

        public DigitalTwinCoordinateRowDispositionResponse ToDisposition(
            string disposition,
            string? targetPointCode,
            string? pointType,
            string reason) =>
            ToDisposition(disposition, targetPointCode, pointType, null, reason);

        private static int? ParseNullableInt(string value, string fieldName, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (TryParseInt(value, out var parsed))
            {
                return parsed;
            }

            errors.Add($"{fieldName} must be an integer");
            return null;
        }

        private static bool TryParseInt(string value, out int parsed) =>
            int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        private static long? ParseMicrometers(string value, string fieldName, List<string> errors)
        {
            if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var mm))
            {
                errors.Add($"{fieldName} must be a decimal millimeter value");
                return null;
            }

            return (long)Math.Round(mm * 1000m, MidpointRounding.AwayFromZero);
        }
    }
}

public static class DigitalTwinCoordinateDisposition
{
    public const string ExecutableTarget = "ExecutableTarget";
    public const string ReferenceOnly = "ReferenceOnly";
    public const string Rejected = "Rejected";
}
