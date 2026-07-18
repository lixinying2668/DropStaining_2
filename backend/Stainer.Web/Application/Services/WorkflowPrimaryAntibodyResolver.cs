namespace Stainer.Web.Application.Services;

using Stainer.Web.Domain.Entities;

public enum PrimaryAntibodyResolutionStatus
{
    Resolved,
    StepMissing,
    CodeEmpty,
    Conflict
}

public sealed record PrimaryAntibodyResolution(
    PrimaryAntibodyResolutionStatus Status,
    string? Code)
{
    public static PrimaryAntibodyResolution Resolved(string code)
        => new(PrimaryAntibodyResolutionStatus.Resolved, code);

    public static PrimaryAntibodyResolution StepMissing()
        => new(PrimaryAntibodyResolutionStatus.StepMissing, null);

    public static PrimaryAntibodyResolution CodeEmpty()
        => new(PrimaryAntibodyResolutionStatus.CodeEmpty, null);

    public static PrimaryAntibodyResolution Conflict()
        => new(PrimaryAntibodyResolutionStatus.Conflict, null);
}

/// <summary>
/// 解析已发布 IHC 染色流程的一抗编码。一抗由流程的「一抗孵育」步骤的 ReagentCode 唯一确定。
/// 识别一抗步骤的口径：步骤 MajorStepCode 以 "PRIMARY" 开头（命中 PRIMARY / PRIMARY_ANTIBODY），
/// 与 WorkflowMaintenanceService 的判定保持一致；不解析 LegacyParametersJson。
/// </summary>
public sealed class WorkflowPrimaryAntibodyResolver
{
    public PrimaryAntibodyResolution Resolve(WorkflowVersion version)
    {
        var primarySteps = (version.Steps ?? Enumerable.Empty<WorkflowStep>())
            .Where(IsPrimaryAntibodyStep)
            .ToList();
        if (primarySteps.Count == 0)
        {
            return PrimaryAntibodyResolution.StepMissing();
        }

        // 任何一个抗孵育步骤的 reagentCode 为空即视为流程配置错误：即使另有一抗步骤带有效编码，
        // 也不应静默忽略空值（否则会把「一抗=001 + 另一步一抗=空」误解析为 001）。
        if (primarySteps.Any(x => string.IsNullOrWhiteSpace(x.ReagentCode)))
        {
            return PrimaryAntibodyResolution.CodeEmpty();
        }

        var codes = primarySteps
            .Select(x => x.ReagentCode!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count > 1)
        {
            return PrimaryAntibodyResolution.Conflict();
        }

        return PrimaryAntibodyResolution.Resolved(codes[0]!);
    }

    /// <summary>
    /// 与 ChannelBatchWorkflowService / PreflightValidationService 等只读场景共用：
    /// 已选流程解析成功则返回一抗 code，否则返回 null（不抛错）。
    /// </summary>
    public string? ResolveCodeOrNull(WorkflowVersion? version)
    {
        if (version is null)
        {
            return null;
        }

        var resolution = Resolve(version);
        return resolution.Status == PrimaryAntibodyResolutionStatus.Resolved
            ? resolution.Code
            : null;
    }

    private static bool IsPrimaryAntibodyStep(WorkflowStep step)
    {
        var majorStepCode = step.MajorStepCode ?? string.Empty;
        return majorStepCode.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase);
    }
}
