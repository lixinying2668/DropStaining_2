namespace Stainer.Web.Application.Requests;

public sealed record CreateMachineRunRequest(
    string CommandId,
    IReadOnlyList<string> StainingTaskIds);

public sealed record RunCommandRequest(string CommandId);

public sealed record InjectFaultRequest(
    string CommandId,
    string Message);

public sealed record RedoMajorStepRequest(
    string CommandId,
    string Reason);
