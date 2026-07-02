namespace Stainer.Web.Domain.Entities;

public sealed class EngineeringSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CommandId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = EngineeringSessionStatus.Active;
    public string Reason { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool DangerousOperationConfirmed { get; set; }
    public DateTimeOffset AuthenticatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
}

public sealed class DeviceCommunicationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceMode { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string CommandId { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? Actor { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public bool Acknowledged { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RequestJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
    public string PersistenceStatus { get; set; } = DeviceCommunicationPersistenceStatus.Pending;
    public string? PersistenceFailureReason { get; set; }
    public int PersistenceAttemptCount { get; set; }
    public DateTimeOffset PersistenceLastAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PersistenceCompletedAtUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class DeviceCommunicationPersistenceStatus
{
    public const string Pending = "Pending";
    public const string Complete = "Complete";
    public const string Failed = "Failed";
}

public static class EngineeringSessionStatus
{
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
}
