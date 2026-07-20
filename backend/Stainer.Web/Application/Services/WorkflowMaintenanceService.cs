using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class WorkflowMaintenanceService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowVersionMaintenanceResponse?> GetVersionAsync(string workflowVersionId, CancellationToken cancellationToken = default)
    {
        var version = await LoadVersionQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken);
        return version is null ? null : ToVersionResponse(version);
    }

    public async Task<IReadOnlyList<PrimaryAntibodyMappingResponse>> ListPrimaryAntibodyMappingsAsync(CancellationToken cancellationToken = default)
    {
        var mappings = await dbContext.PrimaryAntibodyWorkflowMappings
            .AsNoTracking()
            .Include(x => x.WorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .OrderBy(x => x.PrimaryAntibodyCode)
            .ThenBy(x => x.WorkflowVersion!.WorkflowDefinition!.Code)
            .ThenBy(x => x.WorkflowVersion!.VersionNo)
            .ToListAsync(cancellationToken);
        return mappings.Select(ToMappingResponse).ToList();
    }

    public Task<WorkflowDraftMutationResponse> CreateWorkflowAsync(CreateWorkflowRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.create",
            request,
            actor,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var code = RequireValue(request.Code, "code");
                var name = RequireValue(request.Name, "name");
                var workflowType = NormalizeWorkflowType(request.WorkflowType);
                if (await dbContext.WorkflowDefinitions.AnyAsync(x => x.Code == code, cancellationToken))
                {
                    throw new BusinessRuleException("workflow_code_exists", "Workflow code already exists.", StatusCodes.Status409Conflict);
                }

                var definition = new WorkflowDefinition
                {
                    Code = code,
                    Name = name,
                    WorkflowType = workflowType,
                    Description = OptionalValue(request.Description, string.Empty),
                    CreatedAtUtc = now
                };
                var version = new WorkflowVersion
                {
                    WorkflowDefinition = definition,
                    VersionNo = 1,
                    VersionLabel = OptionalValue(request.VersionLabel, "0.1"),
                    Status = WorkflowVersionStatus.Draft,
                    ChangeNote = OptionalValue(request.ChangeNote, "Create initial draft."),
                    CreatedAtUtc = now
                };
                definition.Versions.Add(version);
                dbContext.WorkflowDefinitions.Add(definition);
                AddAudit(actor, "workflow.create", "WorkflowVersion", version.Id, new
                {
                    definition.Code,
                    definition.Name,
                    definition.WorkflowType,
                    version.VersionNo,
                    version.VersionLabel,
                    commandId = request.CommandId
                });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "created");

                var response = ToDraftResponse(request.CommandId, definition, version, "Workflow draft created.");
                return new CommandExecutionResult<WorkflowDraftMutationResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<WorkflowDraftMutationResponse> CreateWorkflowVersionAsync(string workflowId, CreateWorkflowVersionRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.version.create",
            new { workflowId, request },
            actor,
            async () =>
            {
                var workflow = await LoadWorkflowWithVersionsAsync(workflowId, cancellationToken);
                var versionNo = workflow.Versions.Select(x => x.VersionNo).DefaultIfEmpty(0).Max() + 1;
                var versionLabel = OptionalValue(request.VersionLabel, versionNo.ToString());
                EnsureVersionLabelAvailable(workflow, versionLabel);

                var now = DateTimeOffset.UtcNow;
                var version = new WorkflowVersion
                {
                    WorkflowDefinitionId = workflow.Id,
                    VersionNo = versionNo,
                    VersionLabel = versionLabel,
                    Status = WorkflowVersionStatus.Draft,
                    ChangeNote = OptionalValue(request.ChangeNote, "Create blank draft version."),
                    CreatedAtUtc = now
                };
                workflow.Versions.Add(version);
                workflow.UpdatedAtUtc = now;
                AddAudit(actor, "workflow.version.create", "WorkflowVersion", version.Id, new
                {
                    workflow.Id,
                    workflow.Code,
                    version.VersionNo,
                    version.VersionLabel,
                    commandId = request.CommandId
                });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "created");

                var response = ToDraftResponse(request.CommandId, workflow, version, "Workflow draft version created.");
                return new CommandExecutionResult<WorkflowDraftMutationResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<WorkflowDraftMutationResponse> CopyVersionAsDraftAsync(string sourceWorkflowVersionId, CopyWorkflowVersionDraftRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.version.copy_draft",
            new { sourceWorkflowVersionId, request },
            actor,
            async () =>
            {
                var source = await LoadVersionQuery()
                    .SingleOrDefaultAsync(x => x.Id == sourceWorkflowVersionId, cancellationToken)
                    ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
                var workflow = source.WorkflowDefinition ?? throw new BusinessRuleException("workflow_not_found", "Workflow was not found.", StatusCodes.Status404NotFound);
                await dbContext.Entry(workflow).Collection(x => x.Versions).LoadAsync(cancellationToken);
                var versionNo = workflow.Versions.Select(x => x.VersionNo).DefaultIfEmpty(0).Max() + 1;
                var versionLabel = OptionalValue(request.VersionLabel, NextVersionLabel(source, versionNo));
                EnsureVersionLabelAvailable(workflow, versionLabel);

                var now = DateTimeOffset.UtcNow;
                var copy = new WorkflowVersion
                {
                    WorkflowDefinitionId = workflow.Id,
                    VersionNo = versionNo,
                    VersionLabel = versionLabel,
                    Status = WorkflowVersionStatus.Draft,
                    ChangeNote = OptionalValue(request.ChangeNote, $"Copied from v{source.VersionLabel}."),
                    PlanningRulesJson = source.PlanningRulesJson,
                    CreatedAtUtc = now
                };
                foreach (var step in source.Steps.OrderBy(x => x.StepNo))
                {
                    copy.Steps.Add(CopyStep(step, now));
                }

                foreach (var requirement in source.ReagentRequirements.OrderBy(x => x.ReagentCode))
                {
                    copy.ReagentRequirements.Add(new WorkflowReagentRequirement
                    {
                        ReagentCode = requirement.ReagentCode,
                        RequiredVolumeUl = requirement.RequiredVolumeUl,
                        IsRequired = requirement.IsRequired,
                        CreatedAtUtc = now
                    });
                }

                workflow.Versions.Add(copy);
                workflow.UpdatedAtUtc = now;
                AddAudit(actor, "workflow.version.copy_draft", "WorkflowVersion", copy.Id, new
                {
                    sourceWorkflowVersionId = source.Id,
                    workflow.Id,
                    workflow.Code,
                    copy.VersionNo,
                    copy.VersionLabel,
                    commandId = request.CommandId
                });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, copy, "copied");

                var response = ToDraftResponse(request.CommandId, workflow, copy, "Workflow version copied as draft.");
                return new CommandExecutionResult<WorkflowDraftMutationResponse>(response, "WorkflowVersion", copy.Id);
            },
            cancellationToken);
    }

    public Task<CommandResponse> UpdateVersionAsync(string workflowVersionId, UpdateWorkflowVersionRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            request.CommandId,
            "workflow.version.update",
            new { workflowVersionId, request },
            actor,
            workflowVersionId,
            async version =>
            {
                var now = DateTimeOffset.UtcNow;
                var workflow = version.WorkflowDefinition!;
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    workflow.Name = request.Name.Trim();
                }

                if (request.Description is not null)
                {
                    workflow.Description = request.Description.Trim();
                }

                if (request.IsEnabled.HasValue)
                {
                    if (!request.IsEnabled.Value
                        && await dbContext.WorkflowVersions.AnyAsync(
                            x => x.WorkflowDefinitionId == workflow.Id && x.DefaultExperimentType != null,
                            cancellationToken))
                    {
                        throw new BusinessRuleException(
                            "default_workflow_disable_forbidden",
                            "A workflow containing the current default version cannot be disabled. Set another default workflow first.",
                            StatusCodes.Status409Conflict);
                    }

                    workflow.IsEnabled = request.IsEnabled.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.VersionLabel) && request.VersionLabel.Trim() != version.VersionLabel)
                {
                    EnsureVersionLabelAvailable(workflow, request.VersionLabel.Trim(), version.Id);
                    version.VersionLabel = request.VersionLabel.Trim();
                }

                if (request.ChangeNote is not null)
                {
                    version.ChangeNote = request.ChangeNote.Trim();
                }

                if (request.PlanningRulesJson is not null)
                {
                    version.PlanningRulesJson = NormalizeJsonObject(request.PlanningRulesJson);
                }

                workflow.UpdatedAtUtc = now;
                version.UpdatedAtUtc = now;
                AddAudit(actor, "workflow.version.update", "WorkflowVersion", version.Id, new { commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "updated");
                await Task.CompletedTask;
                return "Workflow draft updated.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> AddStepAsync(string workflowVersionId, SaveWorkflowStepRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            request.CommandId,
            "workflow.step.create",
            new { workflowVersionId, request },
            actor,
            workflowVersionId,
            async version =>
            {
                var stepNo = request.StepNo ?? version.Steps.Select(x => x.StepNo).DefaultIfEmpty(0).Max() + 1;
                if (version.Steps.Any(x => x.StepNo == stepNo))
                {
                    throw new BusinessRuleException("workflow_step_order_exists", "Step order already exists.", StatusCodes.Status409Conflict);
                }

                var step = BuildStep(version.Id, request, stepNo);
                await ValidateStepReagentAsync(step.ReagentCode, cancellationToken);
                version.Steps.Add(step);
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.step.create", "WorkflowStep", step.Id, new { workflowVersionId, step.StepNo, commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowStepChanged, version, "created");
                return "Workflow step created.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> UpdateStepAsync(string workflowVersionId, string stepId, SaveWorkflowStepRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            request.CommandId,
            "workflow.step.update",
            new { workflowVersionId, stepId, request },
            actor,
            workflowVersionId,
            async version =>
            {
                var step = version.Steps.SingleOrDefault(x => x.Id == stepId)
                    ?? throw new BusinessRuleException("workflow_step_not_found", "Workflow step was not found.", StatusCodes.Status404NotFound);
                var desiredStepNo = request.StepNo ?? step.StepNo;
                if (desiredStepNo != step.StepNo && version.Steps.Any(x => x.Id != step.Id && x.StepNo == desiredStepNo))
                {
                    throw new BusinessRuleException("workflow_step_order_exists", "Step order already exists.", StatusCodes.Status409Conflict);
                }

                ApplyStep(step, request, desiredStepNo);
                await ValidateStepReagentAsync(step.ReagentCode, cancellationToken);
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.step.update", "WorkflowStep", step.Id, new { workflowVersionId, step.StepNo, commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowStepChanged, version, "updated");
                return "Workflow step updated.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> DeleteStepAsync(string workflowVersionId, string stepId, string commandId, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            commandId,
            "workflow.step.delete",
            new { workflowVersionId, stepId, commandId },
            actor,
            workflowVersionId,
            async version =>
            {
                var step = version.Steps.SingleOrDefault(x => x.Id == stepId)
                    ?? throw new BusinessRuleException("workflow_step_not_found", "Workflow step was not found.", StatusCodes.Status404NotFound);
                dbContext.WorkflowSteps.Remove(step);
                await dbContext.SaveChangesAsync(cancellationToken);
                ResequenceSteps(version.Steps.Where(x => x.Id != stepId));
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.step.delete", "WorkflowStep", step.Id, new { workflowVersionId, commandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowStepChanged, version, "deleted");
                return "Workflow step deleted.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> DeleteDraftAsync(
        string workflowVersionId,
        string commandId,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            commandId,
            "workflow.version.delete_draft",
            new { workflowVersionId, commandId },
            actor,
            async () =>
            {
                var version = await RequireDraftVersionAsync(workflowVersionId, cancellationToken);
                var workflow = version.WorkflowDefinition!;
                dbContext.WorkflowVersions.Remove(version);
                workflow.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.version.delete_draft", "WorkflowVersion", version.Id, new
                {
                    workflow.Id,
                    workflow.Code,
                    version.VersionNo,
                    version.VersionLabel,
                    commandId
                });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "deleted");
                var response = new CommandResponse(true, commandId, false, "Workflow draft deleted.");
                return new CommandExecutionResult<CommandResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<CommandResponse> MoveStepAsync(string workflowVersionId, string stepId, bool moveUp, string commandId, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            commandId,
            moveUp ? "workflow.step.move_up" : "workflow.step.move_down",
            new { workflowVersionId, stepId, moveUp, commandId },
            actor,
            workflowVersionId,
            async version =>
            {
                var ordered = version.Steps.OrderBy(x => x.StepNo).ToList();
                var index = ordered.FindIndex(x => x.Id == stepId);
                if (index < 0)
                {
                    throw new BusinessRuleException("workflow_step_not_found", "Workflow step was not found.", StatusCodes.Status404NotFound);
                }

                var otherIndex = moveUp ? index - 1 : index + 1;
                if (otherIndex < 0 || otherIndex >= ordered.Count)
                {
                    throw new BusinessRuleException("workflow_step_move_out_of_range", "Workflow step cannot be moved further.", StatusCodes.Status409Conflict);
                }

                var current = ordered[index];
                var other = ordered[otherIndex];
                var currentNo = current.StepNo;
                current.StepNo = -current.StepNo;
                await dbContext.SaveChangesAsync(cancellationToken);
                current.StepNo = other.StepNo;
                other.StepNo = currentNo;
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, moveUp ? "workflow.step.move_up" : "workflow.step.move_down", "WorkflowStep", current.Id, new { workflowVersionId, commandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowStepChanged, version, moveUp ? "moveUp" : "moveDown");
                return "Workflow step moved.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> AddRequirementAsync(string workflowVersionId, SaveWorkflowReagentRequirementRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            request.CommandId,
            "workflow.reagent_requirement.create",
            new { workflowVersionId, request },
            actor,
            workflowVersionId,
            async version =>
            {
                var reagentCode = await RequireEnabledReagentCodeAsync(request.ReagentCode, cancellationToken);
                if (version.ReagentRequirements.Any(x => x.ReagentCode == reagentCode))
                {
                    throw new BusinessRuleException("workflow_reagent_requirement_exists", "Reagent requirement already exists.", StatusCodes.Status409Conflict);
                }

                var requirement = new WorkflowReagentRequirement
                {
                    WorkflowVersionId = version.Id,
                    ReagentCode = reagentCode,
                    RequiredVolumeUl = ValidateNonNegative(request.RequiredVolumeUl, "requiredVolumeUl"),
                    IsRequired = request.IsRequired,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                version.ReagentRequirements.Add(requirement);
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.reagent_requirement.create", "WorkflowReagentRequirement", requirement.Id, new { workflowVersionId, reagentCode, commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowReagentRequirementChanged, version, "created");
                return "Workflow reagent requirement created.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> UpdateRequirementAsync(string workflowVersionId, string id, SaveWorkflowReagentRequirementRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            request.CommandId,
            "workflow.reagent_requirement.update",
            new { workflowVersionId, id, request },
            actor,
            workflowVersionId,
            async version =>
            {
                var requirement = version.ReagentRequirements.SingleOrDefault(x => x.Id == id)
                    ?? throw new BusinessRuleException("workflow_reagent_requirement_not_found", "Workflow reagent requirement was not found.", StatusCodes.Status404NotFound);
                var reagentCode = await RequireEnabledReagentCodeAsync(request.ReagentCode, cancellationToken);
                if (reagentCode != requirement.ReagentCode && version.ReagentRequirements.Any(x => x.Id != id && x.ReagentCode == reagentCode))
                {
                    throw new BusinessRuleException("workflow_reagent_requirement_exists", "Reagent requirement already exists.", StatusCodes.Status409Conflict);
                }

                requirement.ReagentCode = reagentCode;
                requirement.RequiredVolumeUl = ValidateNonNegative(request.RequiredVolumeUl, "requiredVolumeUl");
                requirement.IsRequired = request.IsRequired;
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.reagent_requirement.update", "WorkflowReagentRequirement", requirement.Id, new { workflowVersionId, reagentCode, commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowReagentRequirementChanged, version, "updated");
                return "Workflow reagent requirement updated.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> DeleteRequirementAsync(string workflowVersionId, string id, string commandId, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            commandId,
            "workflow.reagent_requirement.delete",
            new { workflowVersionId, id, commandId },
            actor,
            workflowVersionId,
            async version =>
            {
                var requirement = version.ReagentRequirements.SingleOrDefault(x => x.Id == id)
                    ?? throw new BusinessRuleException("workflow_reagent_requirement_not_found", "Workflow reagent requirement was not found.", StatusCodes.Status404NotFound);
                dbContext.WorkflowReagentRequirements.Remove(requirement);
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.reagent_requirement.delete", "WorkflowReagentRequirement", requirement.Id, new { workflowVersionId, requirement.ReagentCode, commandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowReagentRequirementChanged, version, "deleted");
                await Task.CompletedTask;
                return "Workflow reagent requirement deleted.";
            },
            cancellationToken);
    }

    public Task<CommandResponse> RecalculateRequirementsAsync(string workflowVersionId, string commandId, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return RunVersionCommandAsync(
            commandId,
            "workflow.reagent_requirement.recalculate",
            new { workflowVersionId, commandId },
            actor,
            workflowVersionId,
            async version =>
            {
                var grouped = version.Steps
                    .Where(x => !string.IsNullOrWhiteSpace(x.ReagentCode))
                    .GroupBy(x => x.ReagentCode!.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Sum(step => Math.Max(0, step.VolumeUl ?? 0)), StringComparer.Ordinal);

                foreach (var reagentCode in grouped.Keys)
                {
                    await RequireEnabledReagentCodeAsync(reagentCode, cancellationToken);
                }

                foreach (var existing in version.ReagentRequirements.ToList())
                {
                    if (!grouped.ContainsKey(existing.ReagentCode))
                    {
                        dbContext.WorkflowReagentRequirements.Remove(existing);
                    }
                }

                foreach (var (reagentCode, volume) in grouped)
                {
                    var existing = version.ReagentRequirements.SingleOrDefault(x => x.ReagentCode == reagentCode);
                    if (existing is null)
                    {
                        version.ReagentRequirements.Add(new WorkflowReagentRequirement
                        {
                            WorkflowVersionId = version.Id,
                            ReagentCode = reagentCode,
                            RequiredVolumeUl = volume,
                            IsRequired = true,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        });
                    }
                    else
                    {
                        existing.RequiredVolumeUl = volume;
                        existing.IsRequired = true;
                    }
                }

                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.reagent_requirement.recalculate", "WorkflowVersion", version.Id, new { workflowVersionId, commandId, reagentCount = grouped.Count });
                PublishWorkflowEvent(MachineEventTypes.WorkflowReagentRequirementChanged, version, "recalculated");
                return "Workflow reagent requirements recalculated.";
            },
            cancellationToken);
    }

    public async Task<PublishValidationResponse> ValidatePublishAsync(string workflowVersionId, CancellationToken cancellationToken = default)
    {
        var version = await LoadVersionQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
            ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        var issues = await BuildPublishValidationIssuesAsync(version, cancellationToken);
        var failCount = issues.Count(x => x.Severity == "Fail");
        var warningCount = issues.Count(x => x.Severity == "Warning");
        return new PublishValidationResponse(
            version.Id,
            failCount > 0 ? "Fail" : warningCount > 0 ? "Warning" : "Pass",
            failCount,
            warningCount,
            issues);
    }

    public Task<CommandResponse> PublishAsync(string workflowVersionId, PublishWorkflowVersionRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.version.publish",
            new { workflowVersionId, request },
            actor,
            async () =>
            {
                var version = await LoadVersionQuery()
                    .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
                    ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
                if (version.Status != WorkflowVersionStatus.Draft)
                {
                    throw new BusinessRuleException("workflow_publish_requires_draft", "Only Draft workflow versions can be published.", StatusCodes.Status409Conflict);
                }

                var validation = await BuildPublishValidationIssuesAsync(version, cancellationToken);
                if (validation.Any(x => x.Severity == "Fail"))
                {
                    throw new BusinessRuleException("workflow_publish_validation_failed", "Workflow publish validation failed.", StatusCodes.Status409Conflict);
                }

                version.Status = WorkflowVersionStatus.Published;
                version.PublishedAtUtc = DateTimeOffset.UtcNow;
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.version.publish", "WorkflowVersion", version.Id, new { commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "published");
                var response = new CommandResponse(true, request.CommandId, false, "Workflow version published.");
                return new CommandExecutionResult<CommandResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<CommandResponse> RetireAsync(string workflowVersionId, RetireWorkflowVersionRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.version.retire",
            new { workflowVersionId, request },
            actor,
            async () =>
            {
                var reason = RequireValue(request.Reason, "reason");
                var version = await LoadVersionQuery()
                    .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
                    ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
                if (version.Status != WorkflowVersionStatus.Published)
                {
                    throw new BusinessRuleException("workflow_retire_requires_published", "Only Published workflow versions can be retired.", StatusCodes.Status409Conflict);
                }

                if (version.DefaultExperimentType is not null)
                {
                    throw new BusinessRuleException(
                        "default_workflow_retire_forbidden",
                        "The current default workflow cannot be retired. Set another Published default workflow first.",
                        StatusCodes.Status409Conflict);
                }

                version.Status = WorkflowVersionStatus.Retired;
                version.RetiredAtUtc = DateTimeOffset.UtcNow;
                version.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "workflow.version.retire", "WorkflowVersion", version.Id, new { reason, commandId = request.CommandId });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "retired");
                var response = new CommandResponse(true, request.CommandId, false, "Workflow version retired.");
                return new CommandExecutionResult<CommandResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<DefaultWorkflowVersionResponse> SetDefaultAsync(
        string workflowVersionId,
        SetDefaultWorkflowVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "workflow.version.set_default",
            new { workflowVersionId, request },
            actor,
            async () =>
            {
                var version = await LoadVersionQuery()
                    .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
                    ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
                var workflow = version.WorkflowDefinition
                    ?? throw new BusinessRuleException("workflow_not_found", "Workflow definition was not found.", StatusCodes.Status404NotFound);
                if (version.Status != WorkflowVersionStatus.Published)
                {
                    throw new BusinessRuleException("default_workflow_requires_published", "Only Published workflow versions can be set as default.", StatusCodes.Status409Conflict);
                }

                if (!workflow.IsEnabled)
                {
                    throw new BusinessRuleException("default_workflow_disabled", "A disabled workflow cannot be set as default.", StatusCodes.Status409Conflict);
                }

                if (workflow.WorkflowType is not (StainingTaskType.He or StainingTaskType.Ihc))
                {
                    throw new BusinessRuleException("default_workflow_type_invalid", "Only HE or IHC workflows can be set as default.", StatusCodes.Status409Conflict);
                }

                var experimentType = workflow.WorkflowType;
                var requestedExperimentType = (request.ExperimentType ?? string.Empty).Trim().ToUpperInvariant();
                if (requestedExperimentType is not (StainingTaskType.He or StainingTaskType.Ihc))
                {
                    throw new BusinessRuleException("experiment_type_invalid", "ExperimentType must be HE or IHC.", StatusCodes.Status400BadRequest);
                }

                if (requestedExperimentType != experimentType)
                {
                    throw new BusinessRuleException("default_workflow_type_mismatch", "Workflow type does not match the requested default experiment type.", StatusCodes.Status409Conflict);
                }

                var previousDefaults = await dbContext.WorkflowVersions
                    .Include(x => x.WorkflowDefinition)
                    .Where(x => x.DefaultExperimentType == experimentType)
                    .ToListAsync(cancellationToken);
                if (previousDefaults.Count == 1 && previousDefaults[0].Id == version.Id)
                {
                    return new CommandExecutionResult<DefaultWorkflowVersionResponse>(
                        ToDefaultResponse(request.CommandId, version, "Workflow version is already the current default."),
                        "WorkflowVersion",
                        version.Id);
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var previous in previousDefaults.Where(x => x.Id != version.Id))
                {
                    previous.DefaultExperimentType = null;
                    previous.UpdatedAtUtc = now;
                    AddAudit(actor, "workflow.default.unset", "WorkflowVersion", previous.Id, new
                    {
                        experimentType,
                        replacedByWorkflowVersionId = version.Id,
                        commandId = request.CommandId
                    });
                    PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, previous, "defaultUnset");
                }

                version.DefaultExperimentType = experimentType;
                version.UpdatedAtUtc = now;
                AddAudit(actor, "workflow.default.set", "WorkflowVersion", version.Id, new
                {
                    experimentType,
                    previousWorkflowVersionIds = previousDefaults.Where(x => x.Id != version.Id).Select(x => x.Id).ToArray(),
                    commandId = request.CommandId
                });
                PublishWorkflowEvent(MachineEventTypes.WorkflowVersionChanged, version, "defaultSet");
                return new CommandExecutionResult<DefaultWorkflowVersionResponse>(
                    ToDefaultResponse(request.CommandId, version, $"Default {experimentType} workflow updated."),
                    "WorkflowVersion",
                    version.Id);
            },
            cancellationToken);
    }

    public Task<CommandResponse> CreateMappingAsync(CreatePrimaryAntibodyMappingRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "primary_antibody_mapping.create",
            request,
            actor,
            async () =>
            {
                var primaryAntibodyCode = RequireValue(request.PrimaryAntibodyCode, "primaryAntibodyCode");
                var version = await RequirePublishedIhcVersionAsync(request.WorkflowVersionId, cancellationToken);
                var existing = await dbContext.PrimaryAntibodyWorkflowMappings
                    .SingleOrDefaultAsync(x => x.PrimaryAntibodyCode == primaryAntibodyCode && x.WorkflowVersionId == version.Id, cancellationToken);
                if (existing is not null)
                {
                    existing.IsEnabled = true;
                    AddAudit(actor, "primary_antibody_mapping.enable", "PrimaryAntibodyWorkflowMapping", existing.Id, new { primaryAntibodyCode, workflowVersionId = version.Id, commandId = request.CommandId });
                    PublishMappingEvent(existing, "enabled");
                    var existingResponse = new CommandResponse(true, request.CommandId, false, "Primary antibody mapping enabled.");
                    return new CommandExecutionResult<CommandResponse>(existingResponse, "PrimaryAntibodyWorkflowMapping", existing.Id);
                }

                var mapping = new PrimaryAntibodyWorkflowMapping
                {
                    PrimaryAntibodyCode = primaryAntibodyCode,
                    WorkflowVersionId = version.Id,
                    IsEnabled = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.PrimaryAntibodyWorkflowMappings.Add(mapping);
                AddAudit(actor, "primary_antibody_mapping.create", "PrimaryAntibodyWorkflowMapping", mapping.Id, new { primaryAntibodyCode, workflowVersionId = version.Id, commandId = request.CommandId });
                PublishMappingEvent(mapping, "created");
                var response = new CommandResponse(true, request.CommandId, false, "Primary antibody mapping created.");
                return new CommandExecutionResult<CommandResponse>(response, "PrimaryAntibodyWorkflowMapping", mapping.Id);
            },
            cancellationToken);
    }

    public Task<CommandResponse> SetMappingEnabledAsync(string id, ChangePrimaryAntibodyMappingStateRequest request, AuthenticatedUser actor, bool enabled, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            enabled ? "primary_antibody_mapping.enable" : "primary_antibody_mapping.disable",
            new { id, enabled, request },
            actor,
            async () =>
            {
                if (!enabled)
                {
                    _ = RequireValue(request.Reason, "reason");
                }

                var mapping = await dbContext.PrimaryAntibodyWorkflowMappings
                    .Include(x => x.WorkflowVersion)
                    .ThenInclude(x => x!.WorkflowDefinition)
                    .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                    ?? throw new BusinessRuleException("primary_antibody_mapping_not_found", "Primary antibody mapping was not found.", StatusCodes.Status404NotFound);
                if (enabled)
                {
                    _ = await RequirePublishedIhcVersionAsync(mapping.WorkflowVersionId, cancellationToken);
                }

                mapping.IsEnabled = enabled;
                AddAudit(actor, enabled ? "primary_antibody_mapping.enable" : "primary_antibody_mapping.disable", "PrimaryAntibodyWorkflowMapping", mapping.Id, new
                {
                    mapping.PrimaryAntibodyCode,
                    mapping.WorkflowVersionId,
                    reason = request.Reason,
                    commandId = request.CommandId
                });
                PublishMappingEvent(mapping, enabled ? "enabled" : "disabled");
                var response = new CommandResponse(true, request.CommandId, false, enabled ? "Primary antibody mapping enabled." : "Primary antibody mapping disabled.");
                return new CommandExecutionResult<CommandResponse>(response, "PrimaryAntibodyWorkflowMapping", mapping.Id);
            },
            cancellationToken);
    }

    private Task<CommandResponse> RunVersionCommandAsync(
        string commandId,
        string operation,
        object request,
        AuthenticatedUser actor,
        string workflowVersionId,
        Func<WorkflowVersion, Task<string>> mutate,
        CancellationToken cancellationToken)
    {
        return idempotencyService.RunAsync(
            commandId,
            operation,
            request,
            actor,
            async () =>
            {
                var version = await RequireEditableVersionAsync(workflowVersionId, cancellationToken);
                var message = await mutate(version);
                var response = new CommandResponse(true, commandId, false, message);
                return new CommandExecutionResult<CommandResponse>(response, "WorkflowVersion", version.Id);
            },
            cancellationToken);
    }

    private IQueryable<WorkflowVersion> LoadVersionQuery()
    {
        return dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.Steps)
            .Include(x => x.ReagentRequirements);
    }

    private async Task<WorkflowDefinition> LoadWorkflowWithVersionsAsync(string workflowId, CancellationToken cancellationToken)
    {
        return await dbContext.WorkflowDefinitions
            .Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Id == workflowId, cancellationToken)
            ?? throw new BusinessRuleException("workflow_not_found", "Workflow was not found.", StatusCodes.Status404NotFound);
    }

    private async Task<WorkflowVersion> RequireDraftVersionAsync(string workflowVersionId, CancellationToken cancellationToken)
    {
        var version = await LoadVersionQuery()
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
            ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        if (version.Status != WorkflowVersionStatus.Draft)
        {
            throw new BusinessRuleException("workflow_version_not_draft", "Only Draft workflow versions can be modified.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    // 2026-07-20 应需求放开：允许直接编辑 Published 工作流版本的步骤/规则（医生可调已发布流程参数）。
    // 回滚方案：把下面条件改回 `version.Status != WorkflowVersionStatus.Draft`（并改回 RequireDraftVersionAsync）即可恢复"仅 Draft 可编辑"。
    private async Task<WorkflowVersion> RequireEditableVersionAsync(string workflowVersionId, CancellationToken cancellationToken)
    {
        var version = await LoadVersionQuery()
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
            ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        if (version.Status != WorkflowVersionStatus.Draft && version.Status != WorkflowVersionStatus.Published)
        {
            throw new BusinessRuleException("workflow_version_not_editable", "Only Draft or Published workflow versions can be modified.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task<WorkflowVersion> RequirePublishedIhcVersionAsync(string workflowVersionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken)
            ?? throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        if (version.Status != WorkflowVersionStatus.Published || version.WorkflowDefinition?.WorkflowType != StainingTaskType.Ihc)
        {
            throw new BusinessRuleException("primary_antibody_mapping_target_invalid", "Primary antibody mappings must target a Published IHC workflow version.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task<IReadOnlyList<PublishValidationIssueResponse>> BuildPublishValidationIssuesAsync(WorkflowVersion version, CancellationToken cancellationToken)
    {
        var issues = new List<PublishValidationIssueResponse>();
        var workflow = version.WorkflowDefinition;
        if (workflow is null)
        {
            issues.Add(Fail("definition", "workflow_missing", "Workflow definition is missing."));
            return issues;
        }

        if (version.Status != WorkflowVersionStatus.Draft)
        {
            issues.Add(Fail("version", "workflow_version_not_draft", "Only Draft workflow versions can be publish-validated."));
        }

        if (string.IsNullOrWhiteSpace(workflow.Code)) issues.Add(Fail("definition", "workflow_code_required", "Workflow code is required."));
        if (string.IsNullOrWhiteSpace(workflow.Name)) issues.Add(Fail("definition", "workflow_name_required", "Workflow name is required."));
        if (workflow.WorkflowType is not (StainingTaskType.He or StainingTaskType.Ihc)) issues.Add(Fail("definition", "workflow_type_invalid", "Workflow type must be HE or IHC."));
        if (string.IsNullOrWhiteSpace(version.VersionLabel)) issues.Add(Fail("version", "workflow_version_label_required", "Workflow version label is required."));
        try
        {
            _ = DabLifecycleService.ReadDabRatio(version.PlanningRulesJson);
        }
        catch (BusinessRuleException exception)
        {
            issues.Add(Fail("rules", exception.Code, exception.Message));
        }

        var orderedSteps = version.Steps.OrderBy(x => x.StepNo).ToList();
        if (orderedSteps.Count == 0)
        {
            issues.Add(Fail("steps", "workflow_steps_required", "At least one workflow step is required."));
        }

        var duplicateStepNo = orderedSteps.GroupBy(x => x.StepNo).FirstOrDefault(x => x.Count() > 1);
        if (duplicateStepNo is not null)
        {
            issues.Add(Fail("steps", "workflow_step_order_duplicate", $"Step order {duplicateStepNo.Key} is duplicated."));
        }

        for (var index = 0; index < orderedSteps.Count; index++)
        {
            var step = orderedSteps[index];
            if (step.StepNo != index + 1)
            {
                issues.Add(Fail("steps", "workflow_step_order_not_contiguous", "Step orders must be contiguous starting at 1."));
                break;
            }

            ValidateStepFields(step, issues);
        }

        var reagentCodes = orderedSteps
            .Select(x => x.ReagentCode?.Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requirementCodes = version.ReagentRequirements.Select(x => x.ReagentCode.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var allReagentCodes = reagentCodes.Concat(requirementCodes).Distinct(StringComparer.Ordinal).ToArray();
        var enabledReagents = await dbContext.ReagentDefinitions
            .AsNoTracking()
            .Where(x => allReagentCodes.Contains(x.ReagentCode))
            .ToDictionaryAsync(x => x.ReagentCode, x => x.IsEnabled, cancellationToken);
        foreach (var reagentCode in reagentCodes)
        {
            if (!enabledReagents.TryGetValue(reagentCode, out var enabled) || !enabled)
            {
                issues.Add(Fail("reagents", "workflow_step_reagent_invalid", $"Step reagent {reagentCode} does not exist or is disabled."));
            }

            if (!requirementCodes.Contains(reagentCode))
            {
                issues.Add(Fail("requirements", "workflow_reagent_requirement_missing", $"Required reagent {reagentCode} is referenced by steps but missing from requirements."));
            }
        }

        foreach (var requirement in version.ReagentRequirements)
        {
            var code = requirement.ReagentCode.Trim().ToUpperInvariant();
            if (!enabledReagents.TryGetValue(code, out var enabled) || !enabled)
            {
                issues.Add(Fail("requirements", "workflow_requirement_reagent_invalid", $"Requirement reagent {code} does not exist or is disabled."));
            }

            if (requirement.RequiredVolumeUl < 0)
            {
                issues.Add(Fail("requirements", "workflow_requirement_volume_invalid", $"Requirement reagent {code} has negative volume."));
            }
        }

        ValidateDomainSteps(workflow.WorkflowType, orderedSteps, issues);
        return issues;
    }

    private static void ValidateStepFields(WorkflowStep step, List<PublishValidationIssueResponse> issues)
    {
        if (string.IsNullOrWhiteSpace(step.StepName)) issues.Add(Fail("steps", "workflow_step_name_required", $"Step {step.StepNo} name is required."));
        if (string.IsNullOrWhiteSpace(step.ActionType)) issues.Add(Fail("steps", "workflow_step_action_required", $"Step {step.StepNo} action type is required."));
        if (step.VolumeUl < 0) issues.Add(Fail("steps", "workflow_step_volume_invalid", $"Step {step.StepNo} volume cannot be negative."));
        if (step.DurationSeconds < 0) issues.Add(Fail("steps", "workflow_step_duration_invalid", $"Step {step.StepNo} duration cannot be negative."));
        if (step.TargetTemperatureDeciC is < 0 or > 1000) issues.Add(Fail("steps", "workflow_step_temperature_invalid", $"Step {step.StepNo} temperature is outside the allowed 0-100C range."));
    }

    private static void ValidateDomainSteps(string workflowType, IReadOnlyList<WorkflowStep> steps, List<PublishValidationIssueResponse> issues)
    {
        if (workflowType == StainingTaskType.He)
        {
            if (!steps.Any(x => HasAny(x, "HEMATOXYLIN", "HEM")))
            {
                issues.Add(Fail("he", "he_hematoxylin_missing", "HE workflow must contain a hematoxylin step."));
            }

            if (!steps.Any(x => HasAny(x, "TERMINAL_WASH", "FINAL_WASH", "WASH")))
            {
                issues.Add(Fail("he", "he_terminal_wash_missing", "HE workflow must contain a terminal wash step."));
            }

            if (steps.Any(x => HasAny(x, "PRIMARY_ANTIBODY", "SECONDARY_ANTIBODY", "DAB")))
            {
                issues.Add(Fail("he", "he_contains_ihc_step", "HE workflow cannot contain IHC-only antibody or DAB steps."));
            }
        }

        if (workflowType == StainingTaskType.Ihc)
        {
            var required = new[]
            {
                ("ihc_blocking_missing", "blocking", new[] { "BLOCK", "BLOCKING" }),
                ("ihc_primary_missing", "primary antibody", new[] { "PRIMARY", "PRIMARY_ANTIBODY" }),
                ("ihc_wash_missing", "wash", new[] { "WASH" }),
                ("ihc_secondary_missing", "secondary antibody", new[] { "SECONDARY", "SECONDARY_ANTIBODY" }),
                ("ihc_dab_missing", "DAB", new[] { "DAB" }),
                ("ihc_hematoxylin_missing", "hematoxylin", new[] { "HEM", "HEMATOXYLIN" }),
                ("ihc_final_wash_missing", "final wash", new[] { "FINAL_WASH", "TERMINAL_WASH" })
            };
            foreach (var (code, label, terms) in required)
            {
                if (!steps.Any(x => HasAny(x, terms)))
                {
                    issues.Add(Fail("ihc", code, $"IHC workflow must contain a {label} step."));
                }
            }
        }
    }

    private static bool HasAny(WorkflowStep step, params string[] terms)
    {
        var text = $"{step.MajorStepCode} {step.StepName} {step.ActionType} {step.ReagentCode}".ToUpperInvariant();
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static WorkflowStep BuildStep(string workflowVersionId, SaveWorkflowStepRequest request, int stepNo)
    {
        var step = new WorkflowStep
        {
            WorkflowVersionId = workflowVersionId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        ApplyStep(step, request, stepNo);
        return step;
    }

    private static void ApplyStep(WorkflowStep step, SaveWorkflowStepRequest request, int stepNo)
    {
        step.StepNo = stepNo;
        step.StepName = RequireValue(request.StepName, "stepName");
        step.ActionType = RequireValue(request.ActionType, "actionType");
        step.MajorStepCode = OptionalValue(request.MajorStepCode, step.ActionType).Trim().ToUpperInvariant().Replace(' ', '_');
        step.ReagentCode = NormalizeOptionalUpper(request.ReagentCode);
        step.VolumeUl = ValidateNonNegative(request.VolumeUl, "volumeUl");
        step.DurationSeconds = ValidateNonNegative(request.DurationSeconds, "durationSeconds");
        step.TargetTemperatureDeciC = request.TargetTemperatureDeciC;
        if (step.TargetTemperatureDeciC is < 0 or > 1000)
        {
            throw new BusinessRuleException("target_temperature_invalid", "Temperature must be between 0 and 100C.");
        }

        step.MixParametersJson = NormalizeJsonObject(request.MixParametersJson);
        step.WashParametersJson = NormalizeJsonObject(request.WashParametersJson);
        step.LegacyParametersJson = NormalizeJsonObject(request.LegacyParametersJson);
        step.FailureStrategy = OptionalValue(request.FailureStrategy, "Stop");
        step.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static WorkflowStep CopyStep(WorkflowStep source, DateTimeOffset now)
    {
        return new WorkflowStep
        {
            StepNo = source.StepNo,
            MajorStepCode = source.MajorStepCode,
            StepName = source.StepName,
            ActionType = source.ActionType,
            ReagentCode = source.ReagentCode,
            VolumeUl = source.VolumeUl,
            DurationSeconds = source.DurationSeconds,
            TargetTemperatureDeciC = source.TargetTemperatureDeciC,
            MixParametersJson = source.MixParametersJson,
            WashParametersJson = source.WashParametersJson,
            LegacyParametersJson = source.LegacyParametersJson,
            FailureStrategy = source.FailureStrategy,
            CreatedAtUtc = now
        };
    }

    private static void ResequenceSteps(IEnumerable<WorkflowStep> steps)
    {
        var next = 1;
        foreach (var step in steps.OrderBy(x => x.StepNo))
        {
            step.StepNo = next++;
            step.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private async Task ValidateStepReagentAsync(string? reagentCode, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(reagentCode))
        {
            _ = await RequireEnabledReagentCodeAsync(reagentCode, cancellationToken);
        }
    }

    private async Task<string> RequireEnabledReagentCodeAsync(string reagentCode, CancellationToken cancellationToken)
    {
        var normalized = RequireValue(reagentCode, "reagentCode").ToUpperInvariant();
        if (!await dbContext.ReagentDefinitions.AnyAsync(x => x.ReagentCode == normalized && x.IsEnabled, cancellationToken))
        {
            throw new BusinessRuleException("reagent_not_found", $"Reagent {normalized} was not found or is disabled.", StatusCodes.Status409Conflict);
        }

        return normalized;
    }

    private static WorkflowDraftMutationResponse ToDraftResponse(string commandId, WorkflowDefinition definition, WorkflowVersion version, string message)
    {
        return new WorkflowDraftMutationResponse(
            true,
            commandId,
            false,
            definition.Id,
            version.Id,
            definition.Code,
            definition.Name,
            version.VersionNo,
            version.VersionLabel,
            version.Status,
            message);
    }

    private static WorkflowVersionMaintenanceResponse ToVersionResponse(WorkflowVersion version)
    {
        var definition = version.WorkflowDefinition!;
        return new WorkflowVersionMaintenanceResponse(
            definition.Id,
            version.Id,
            definition.Code,
            definition.Name,
            definition.WorkflowType,
            definition.Description,
            definition.IsEnabled,
            version.VersionNo,
            version.VersionLabel,
            version.Status,
            version.ChangeNote,
            version.PublishedAtUtc,
            version.RetiredAtUtc,
            version.Steps.OrderBy(x => x.StepNo).Select(ToStepResponse).ToList(),
            version.ReagentRequirements.OrderBy(x => x.ReagentCode).Select(x => new WorkflowReagentRequirementResponse(x.Id, x.ReagentCode, null, x.RequiredVolumeUl, x.IsRequired)).ToList(),
            version.DefaultExperimentType,
            version.PlanningRulesJson);
    }

    private static DefaultWorkflowVersionResponse ToDefaultResponse(string commandId, WorkflowVersion version, string message)
    {
        var workflow = version.WorkflowDefinition!;
        return new DefaultWorkflowVersionResponse(
            true,
            commandId,
            false,
            workflow.WorkflowType,
            workflow.Id,
            version.Id,
            workflow.Code,
            workflow.Name,
            version.VersionLabel,
            message);
    }

    private static WorkflowStepResponse ToStepResponse(WorkflowStep step)
    {
        return new WorkflowStepResponse(
            step.Id,
            step.StepNo,
            step.MajorStepCode,
            step.StepName,
            step.ActionType,
            step.ReagentCode,
            step.VolumeUl,
            step.DurationSeconds,
            step.TargetTemperatureDeciC,
            step.FailureStrategy,
            step.MixParametersJson,
            step.WashParametersJson,
            step.LegacyParametersJson);
    }

    private static PrimaryAntibodyMappingResponse ToMappingResponse(PrimaryAntibodyWorkflowMapping mapping)
    {
        var version = mapping.WorkflowVersion!;
        var workflow = version.WorkflowDefinition!;
        return new PrimaryAntibodyMappingResponse(
            mapping.Id,
            mapping.PrimaryAntibodyCode,
            mapping.WorkflowVersionId,
            workflow.Code,
            workflow.Name,
            version.VersionLabel,
            version.Status,
            mapping.IsEnabled,
            mapping.CreatedAtUtc);
    }

    private static PublishValidationIssueResponse Fail(string area, string code, string message)
    {
        return new PublishValidationIssueResponse("Fail", area, code, message);
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

    private void PublishWorkflowEvent(string type, WorkflowVersion version, string action)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            type,
            null,
            "WorkflowVersion",
            version.Id,
            null,
            new Dictionary<string, object?>
            {
                ["workflowVersionId"] = version.Id,
                ["workflowDefinitionId"] = version.WorkflowDefinitionId,
                ["status"] = version.Status,
                ["defaultExperimentType"] = version.DefaultExperimentType,
                ["action"] = action
            }));
    }

    private void PublishMappingEvent(PrimaryAntibodyWorkflowMapping mapping, string action)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.PrimaryAntibodyMappingChanged,
            null,
            "PrimaryAntibodyWorkflowMapping",
            mapping.Id,
            null,
            new Dictionary<string, object?>
            {
                ["primaryAntibodyMappingId"] = mapping.Id,
                ["primaryAntibodyCode"] = mapping.PrimaryAntibodyCode,
                ["workflowVersionId"] = mapping.WorkflowVersionId,
                ["isEnabled"] = mapping.IsEnabled,
                ["action"] = action
            }));
    }

    private static void EnsureVersionLabelAvailable(WorkflowDefinition workflow, string versionLabel, string? currentVersionId = null)
    {
        if (workflow.Versions.Any(x => x.Id != currentVersionId && x.VersionLabel == versionLabel))
        {
            throw new BusinessRuleException("workflow_version_label_exists", "Workflow version label already exists.", StatusCodes.Status409Conflict);
        }
    }

    private static string NextVersionLabel(WorkflowVersion sourceVersion, int versionNo)
    {
        var label = sourceVersion.VersionLabel.Trim();
        var parts = label.Split('.', 2);
        return int.TryParse(parts[0], out var major)
            ? $"{major + 1}.0"
            : versionNo.ToString();
    }

    private static string NormalizeWorkflowType(string? value)
    {
        var workflowType = RequireValue(value, "workflowType").ToUpperInvariant();
        if (workflowType is not (StainingTaskType.He or StainingTaskType.Ihc))
        {
            throw new BusinessRuleException("workflow_type_invalid", "Workflow type must be HE or IHC.");
        }

        return workflowType;
    }

    private static string? NormalizeOptionalUpper(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToUpperInvariant();
    }

    private static int? ValidateNonNegative(int? value, string fieldName)
    {
        if (value < 0)
        {
            throw new BusinessRuleException($"{fieldName}_invalid", $"{fieldName} cannot be negative.");
        }

        return value;
    }

    private static string NormalizeJsonObject(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BusinessRuleException("json_object_required", "JSON parameters must be objects.");
            }
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("json_invalid", "JSON parameters are invalid.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string RequireValue(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException($"{fieldName}_required", $"{fieldName} is required.");
        }

        return normalized;
    }

    private static string OptionalValue(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
