namespace IncidentFactory.Api.HardScenarios;

public enum HardScenarioRunStatus
{
    Completed,
    Faulted,
    Canceled,
    NotFound
}

public sealed record HardScenarioView(
    string Id,
    string Name,
    string Summary,
    string BusinessPath,
    string OperationName,
    string Tier);

public sealed record HardScenarioRunOutcome(
    string ScenarioId,
    string? RunId,
    HardScenarioRunStatus Status,
    int HttpStatusCode,
    object Body);

public sealed record HardScenarioSuccessResponse(
    bool Ok,
    string ScenarioId,
    string RunId,
    string OperationName,
    string Tier,
    object Result,
    DateTimeOffset ObservedAtUtc);

public sealed record HardScenarioFaultResponse(
    bool Ok,
    string ScenarioId,
    string RunId,
    string OperationName,
    string Tier,
    string ErrorType,
    string ErrorMessage,
    int HttpStatusCode,
    bool IncidentDeliveryAttempted,
    bool IncidentDelivered,
    string? IncidentDeliveryError);

public sealed record HardScenarioCanceledResponse(
    bool Ok,
    string ScenarioId,
    string RunId,
    string OperationName,
    string Tier,
    string Message);


