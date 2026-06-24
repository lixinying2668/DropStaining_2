using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class CommandIdempotencyService(StainerDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> RunAsync<T>(
        string commandId,
        string operation,
        object request,
        AuthenticatedUser actor,
        Func<Task<CommandExecutionResult<T>>> execute,
        CancellationToken cancellationToken = default)
        where T : class
    {
        commandId = RequireCommandId(commandId);
        var requestHash = HashRequest(operation, request);

        var existing = await dbContext.CommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Operation != operation || existing.RequestHash != requestHash)
            {
                throw new BusinessRuleException("command_conflict", "commandId already exists for a different request.", StatusCodes.Status409Conflict);
            }

            var response = JsonSerializer.Deserialize<T>(existing.ResponseJson, JsonOptions);
            if (response is null)
            {
                throw new BusinessRuleException("command_replay_failed", "Stored command response could not be replayed.", StatusCodes.Status409Conflict);
            }

            return MarkReplay(response);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var receipt = new CommandReceipt
        {
            CommandId = commandId,
            Operation = operation,
            RequestHash = requestHash,
            Status = "Started",
            ActorUserId = actor.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.CommandReceipts.Add(receipt);

        var result = await execute();
        receipt.Status = "Completed";
        receipt.CompletedAtUtc = DateTimeOffset.UtcNow;
        receipt.EntityType = result.EntityType;
        receipt.EntityId = result.EntityId;
        receipt.ResponseJson = JsonSerializer.Serialize(result.Response, JsonOptions);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result.Response;
    }

    private static string RequireCommandId(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new BusinessRuleException("command_id_required", "commandId is required.", StatusCodes.Status400BadRequest);
        }

        return commandId.Trim();
    }

    private static string HashRequest(string operation, object request)
    {
        var json = JsonSerializer.Serialize(new { operation, request }, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static T MarkReplay<T>(T response)
        where T : class
    {
        return response switch
        {
            UserMutationResponse x => x with { Replayed = true } as T ?? response,
            TaskCreationResponse x => x with { Replayed = true } as T ?? response,
            ReagentScanConfirmationResponse x => x with { Replayed = true } as T ?? response,
            EngineeringWriteResponse x => x with { Replayed = true } as T ?? response,
            MachineRunResponse x => x with { Replayed = true } as T ?? response,
            RunCommandResponse x => x with { Replayed = true } as T ?? response,
            CommandResponse x => x with { Replayed = true } as T ?? response,
            _ => response
        };
    }
}
