using System.Text.Json.Serialization;

namespace Stainer.Web.Infrastructure.Web;

public sealed class MockRuntimeStore
{
    private readonly object syncRoot = new();
    private readonly List<MockUser> users =
    [
        new("operator", "operator", "Operator", true),
        new("admin", "admin", "Administrator", true)
    ];

    private MockRuntimeState state = CreateDefaultState();

    public MockRuntimeState GetState()
    {
        lock (syncRoot)
        {
            return state;
        }
    }

    public IReadOnlyList<MockUser> GetUsers() => users;

    public object? Authenticate(string username, string password, string role)
    {
        var user = users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)
            && x.Role == role
            && password == "123456"
            && x.Enabled);
        if (user is null)
        {
            return null;
        }

        lock (syncRoot)
        {
            state.ActiveUser = user;
            AddLog($"User login: {user.DisplayName} / {user.Role}");
        }

        return user;
    }

    public void Logout()
    {
        lock (syncRoot)
        {
            state.ActiveUser = null;
            AddLog("User logout");
        }
    }

    public MockRuntimeState Initialize()
    {
        lock (syncRoot)
        {
            state.Initialized = true;
            state.Status = "initialized";
            state.System.RoboticArmHome = true;
            state.System.ReagentCooling = true;
            state.System.ScannerOnline = true;
            state.System.LiquidSensor = true;
            state.System.NeedleWash = true;
            state.System.PureWaterOk = true;
            AddLog("System initialized: arm, cooling, scanner, liquid sensor and needle wash are ready");
            return state;
        }
    }

    public MockRuntimeState Reset()
    {
        lock (syncRoot)
        {
            var activeUser = state.ActiveUser;
            state = CreateDefaultState();
            state.ActiveUser = activeUser;
            AddLog("Runtime state reset");
            return state;
        }
    }

    public MockRuntimeState ScanSamples(int count)
    {
        lock (syncRoot)
        {
            count = Math.Clamp(count, 1, 16);
            state.Channels = CreateDefaultChannels();
            var slideNo = 1;
            foreach (var channel in state.Channels)
            {
                for (var slot = 1; slot <= 4 && slideNo <= count; slot++)
                {
                    channel.Slides.Add(new MockSlide
                    {
                        Id = $"S{slideNo:00}",
                        Channel = channel.Id,
                        Slot = slot,
                        Barcode = $"SLIDE-{DateTimeOffset.Now:MMdd}-{slideNo:00}",
                        ProtocolCode = "IHC",
                        AntibodyCode = slideNo % 2 == 0 ? "AB-CK" : "AB-DEFAULT",
                        PrimaryVolumeUl = 80,
                        TemperatureC = 42,
                        Status = "loaded",
                        CurrentStep = "Waiting for confirmation",
                        Progress = 0
                    });
                    channel.Status = "loaded";
                    channel.CurrentStep = "Loaded";
                    slideNo++;
                }
            }

            if (state.Initialized)
            {
                state.Status = "ready";
            }

            AddLog($"Sample area scan completed: {count} slides");
            return state;
        }
    }

    public MockRuntimeState ScanReagents()
    {
        lock (syncRoot)
        {
            state.Reagents =
            [
                CreateReagent("R1", "00120026062301001", "BLOCK", "Block", "common", 20),
                CreateReagent("R2", "01010026062301001", "AB-DEFAULT", "Primary Default", "primary", 10),
                CreateReagent("R3", "01110026062301002", "AB-CK", "Primary CK", "primary", 10),
                CreateReagent("R4", "S0120026062301001", "SECONDARY", "Secondary A", "secondary", 20),
                CreateReagent("R5", "D0150026062301001", "DAB-A", "DAB A", "dab", 5),
                CreateReagent("R6", "D0250026062301001", "DAB-B", "DAB B", "dab", 5),
                CreateReagent("R7", "W0150026062301001", "WATER", "Water", "water", 50),
                CreateReagent("R8", "H0115026062301001", "HEMATOXYLIN", "Hematoxylin", "common", 15),
                CreateReagent("R9", "C0199926062301001", "WASH", "Wash", "wash", 100),
                CreateReagent("R10", "S0120026062301002", "SECONDARY", "Secondary B", "secondary", 20)
            ];
            AddLog($"Reagent rack scan completed: {state.Reagents.Count} positions");
            return state;
        }
    }

    public MockRuntimeState ConfigureSlide(SlideConfigureRequest request)
    {
        lock (syncRoot)
        {
            var slide = state.Channels.SelectMany(x => x.Slides).FirstOrDefault(x => x.Id == request.SlideId);
            if (slide is null)
            {
                throw new KeyNotFoundException($"slide not found: {request.SlideId}");
            }

            slide.ProtocolCode = request.ProtocolCode;
            slide.AntibodyCode = request.AntibodyCode;
            slide.PrimaryVolumeUl = request.PrimaryVolumeUl;
            slide.TemperatureC = request.TemperatureC;
            slide.Status = "configured";
            slide.CurrentStep = "Configured";
            AddLog($"Slide configured: {slide.Id} {slide.ProtocolCode} {slide.AntibodyCode}");
            return state;
        }
    }

    public MockRuntimeState RunAction(string action)
    {
        lock (syncRoot)
        {
            state.Status = action switch
            {
                "start" => "running",
                "pause" => "paused",
                "resume" => "running",
                "stop" => "stopped",
                _ => state.Status
            };

            if (action == "start" && string.IsNullOrWhiteSpace(state.RunId))
            {
                state.RunId = $"RUN-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..26];
            }

            foreach (var channel in state.Channels.Where(x => x.Slides.Count > 0))
            {
                channel.Status = state.Status == "running" ? "running" : channel.Status;
                channel.Progress = state.Status == "running" ? Math.Max(channel.Progress, 8) : channel.Progress;
                channel.CurrentStep = state.Status == "running" ? "Mock running" : channel.CurrentStep;
            }

            AddLog($"Run command: {action}");
            return state;
        }
    }

    public object EngineerCommand(EngineerCommandRequest request)
    {
        lock (syncRoot)
        {
            var message = $"Mock command executed: {request.Module}/{request.Action}";
            AddLog(message);
            return new
            {
                ok = true,
                request.Module,
                request.Action,
                request.Channel,
                request.Position,
                request.VolumeUl,
                request.DurationS,
                request.TemperatureC,
                message,
                completedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public object GetDab(int? slideCount)
    {
        var count = slideCount ?? GetState().Channels.SelectMany(x => x.Slides).Count(x => x.ProtocolCode == "IHC");
        var totalMl = Math.Round((count * 0.2m) + 0.4m, 2);
        return new
        {
            slideCount = count,
            extraSlideEquivalent = 2,
            totalMl,
            dabAMl = Math.Round(totalMl / 20, 2),
            dabBMl = Math.Round(totalMl / 20, 2),
            pureWaterMl = Math.Round(totalMl * 18 / 20, 2)
        };
    }

    public object GetProtocols()
    {
        return new[]
        {
            new { code = "IHC", name = "IHC", version = "0.1", description = "Mock IHC workflow" },
            new { code = "HE", name = "HE", version = "0.1", description = "Mock HE workflow" }
        };
    }

    public object SystemInfo()
    {
        return new
        {
            app = "Stainer ASP.NET Core Web Host",
            uiHost = "ASP.NET Core",
            pythonRuntimeRequired = false,
            dataSource = "Mock runtime state; SQLite remains a reliable copy",
            urls = new[] { "http://127.0.0.1:5205" },
            timeUtc = DateTimeOffset.UtcNow
        };
    }

    private static MockRuntimeState CreateDefaultState()
    {
        return new MockRuntimeState
        {
            Status = "idle",
            Initialized = false,
            Channels = CreateDefaultChannels(),
            Logs = ["ASP.NET Core web host started"]
        };
    }

    private static List<MockChannel> CreateDefaultChannels()
    {
        return Enumerable.Range(1, 4)
            .Select(x => new MockChannel { Id = x, Name = $"Channel{x}", Status = "empty", CurrentStep = "Empty" })
            .ToList();
    }

    private static MockReagent CreateReagent(string position, string barcode, string code, string name, string type, decimal volumeMl)
    {
        return new MockReagent
        {
            Position = position,
            Barcode = barcode,
            Name = name,
            Code = code,
            ReagentType = type,
            VolumeMl = volumeMl,
            MinAlarmMl = 1,
            Available = true,
            LotNo = "LOT202606",
            ExpireDate = "2027-06-01"
        };
    }

    private void AddLog(string message)
    {
        state.Logs.Insert(0, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        if (state.Logs.Count > 300)
        {
            state.Logs.RemoveRange(300, state.Logs.Count - 300);
        }
    }
}

public sealed class MockRuntimeState
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public bool Initialized { get; set; }
    public MockSystemCheck System { get; set; } = new();
    public List<MockChannel> Channels { get; set; } = [];
    public List<MockReagent> Reagents { get; set; } = [];
    public MockUser? ActiveUser { get; set; }
    public List<string> Alarms { get; set; } = [];
    public List<string> Logs { get; set; } = [];
}

public sealed class MockSystemCheck
{
    public bool RoboticArmHome { get; set; }
    public bool ReagentCooling { get; set; }
    public bool ScannerOnline { get; set; }
    public bool LiquidSensor { get; set; }
    public bool NeedleWash { get; set; }
    public bool PureWaterOk { get; set; } = true;
    public bool WasteTankFull { get; set; }
    public bool ToxicTankFull { get; set; }
    public decimal CurrentTemperatureC { get; set; } = 42;
    public decimal ReagentTemperatureC { get; set; } = 8;
}

public sealed class MockChannel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "empty";
    public int Progress { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<MockSlide> Slides { get; set; } = [];
    public bool Selected { get; set; }
}

public sealed class MockSlide
{
    public string Id { get; set; } = string.Empty;
    public int Channel { get; set; }
    public int Slot { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string ProtocolCode { get; set; } = "IHC";
    public string AntibodyCode { get; set; } = "AB-DEFAULT";
    public decimal PrimaryVolumeUl { get; set; } = 80;
    public decimal TemperatureC { get; set; } = 42;
    public string Status { get; set; } = "loaded";
    public string CurrentStep { get; set; } = "Waiting";
    public int Progress { get; set; }
    public string? Error { get; set; }
}

public sealed class MockReagent
{
    public string Position { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ReagentType { get; set; } = string.Empty;
    public decimal VolumeMl { get; set; }
    public decimal MinAlarmMl { get; set; }
    public bool Available { get; set; } = true;
    public string? LotNo { get; set; }
    public string? ExpireDate { get; set; }
}

public sealed record MockUser(string Username, string Role, string DisplayName, bool Enabled);

public sealed record LoginRequest(string Username, string Password, string Role);

public sealed record SlideConfigureRequest(
    [property: JsonPropertyName("slide_id")] string SlideId,
    [property: JsonPropertyName("protocol_code")] string ProtocolCode,
    [property: JsonPropertyName("antibody_code")] string AntibodyCode,
    [property: JsonPropertyName("primary_volume_ul")] decimal PrimaryVolumeUl,
    [property: JsonPropertyName("temperature_c")] decimal TemperatureC);

public sealed record EngineerCommandRequest(
    string Module,
    string Action,
    int? Channel,
    string? Position,
    [property: JsonPropertyName("volume_ul")] decimal? VolumeUl,
    decimal? Speed,
    [property: JsonPropertyName("duration_s")] int? DurationS,
    [property: JsonPropertyName("temperature_c")] decimal? TemperatureC,
    Dictionary<string, object>? Payload);
