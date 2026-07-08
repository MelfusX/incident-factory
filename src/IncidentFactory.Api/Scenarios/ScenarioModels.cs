using System.Text.Json;
using IncidentFactory.Api.IncidentCompass;
using Microsoft.AspNetCore.Http;

namespace IncidentFactory.Api.Scenarios;

public enum ScenarioRunSource
{
    Manual,
    Schedule
}

public enum ScenarioRunStatus
{
    Faulted,
    Canceled,
    Skipped
}

public sealed record ScenarioCatalog(IReadOnlyList<ScenarioDefinition> Scenarios)
{
    public static ScenarioCatalog Create()
    {
        return new ScenarioCatalog([
            new ScenarioDefinition(
                ScenarioIds.LatencySpike,
                "Latency spike",
                "Checkout responds slowly, then times out with a stable 504 signal.",
                "/api/fake/checkout",
                "checkout.submit",
                new ScenarioSignalContract("TimeoutException", StatusCodes.Status504GatewayTimeout, true),
                [
                    new ScenarioParameterDefinition("latencyMs", "Latency", "number", "ms", 50, 30000, 2500)
                ]),
            new ScenarioDefinition(
                ScenarioIds.UnhandledException,
                "Unhandled exception",
                "Payment authorization throws an unhandled business exception and returns a stable 500 signal.",
                "/api/fake/payments/authorize",
                "payments.authorize",
                new ScenarioSignalContract("InvalidOperationException", StatusCodes.Status500InternalServerError, true),
                [
                    new ScenarioParameterDefinition("exceptionMessage", "Message", "text", null, null, null, "Payment authorization state was invalid.")
                ]),
            new ScenarioDefinition(
                ScenarioIds.NullReference,
                "Null reference",
                "Customer profile lookup dereferences a missing profile and returns a stable 500 signal.",
                "/api/fake/customers/profile",
                "customers.profile",
                new ScenarioSignalContract("NullReferenceException", StatusCodes.Status500InternalServerError, true),
                [
                    new ScenarioParameterDefinition("customerId", "Customer", "text", null, null, null, "cust-missing-404")
                ]),
            new ScenarioDefinition(
                ScenarioIds.SerializationError,
                "Serialization error",
                "Order import accepts unvalidated input and fails during JSON deserialization.",
                "/api/fake/orders/import",
                "orders.import",
                new ScenarioSignalContract("JsonException", StatusCodes.Status500InternalServerError, true),
                [
                    new ScenarioParameterDefinition("rawOrderJson", "Raw JSON", "textarea", null, null, null, "{\"orderId\":")
                ]),
            new ScenarioDefinition(
                ScenarioIds.RetryStorm,
                "Retry storm",
                "Inventory reservation burns through repeated failing downstream attempts before surfacing a 503.",
                "/api/fake/inventory/reserve",
                "inventory.reserve",
                new ScenarioSignalContract("HttpRequestException", StatusCodes.Status503ServiceUnavailable, true),
                [
                    new ScenarioParameterDefinition("attempts", "Attempts", "number", null, 1, 20, 5),
                    new ScenarioParameterDefinition("retryDelayMs", "Retry delay", "number", "ms", 0, 5000, 200)
                ]),
            new ScenarioDefinition(
                ScenarioIds.BrokenDownstreamCall,
                "Broken downstream call",
                "Fulfillment dispatch depends on an unavailable downstream service and returns a 502.",
                "/api/fake/fulfillment/dispatch",
                "fulfillment.dispatch",
                new ScenarioSignalContract("HttpRequestException", StatusCodes.Status502BadGateway, true),
                [
                    new ScenarioParameterDefinition("downstreamHost", "Downstream", "text", null, null, null, "http://shipping-gateway.local")
                ]),
            new ScenarioDefinition(
                ScenarioIds.SimulatedDbTimeout,
                "Simulated DB timeout",
                "Month-end report generation simulates a database wait and returns a 504 timeout signal.",
                "/api/fake/reports/month-end",
                "reports.monthEnd",
                new ScenarioSignalContract("TimeoutException", StatusCodes.Status504GatewayTimeout, true),
                [
                    new ScenarioParameterDefinition("timeoutMs", "Timeout", "number", "ms", 50, 30000, 3000)
                ]),
            new ScenarioDefinition(
                ScenarioIds.BadConfigMisleadingSymptom,
                "Bad config symptom",
                "Recommendation rendering reads unsafe configuration and fails later with a misleading null reference.",
                "/api/fake/recommendations/render",
                "recommendations.render",
                new ScenarioSignalContract("NullReferenceException", StatusCodes.Status500InternalServerError, true),
                [
                    new ScenarioParameterDefinition("configKey", "Config key", "text", null, null, null, "recommendations.primaryFormatter")
                ])
        ]);
    }
}

public sealed record ScenarioDefinition(
    string Id,
    string Name,
    string Summary,
    string BusinessPath,
    string OperationName,
    ScenarioSignalContract Signal,
    IReadOnlyList<ScenarioParameterDefinition> Parameters);

public sealed record ScenarioSignalContract(
    string ErrorType,
    int HttpStatusCode,
    bool RequiresDurationMs);

public sealed record ScenarioParameterDefinition(
    string Name,
    string Label,
    string Kind,
    string? Unit,
    int? Min,
    int? Max,
    object DefaultValue);

public sealed record ScenarioTriggerRequest(Dictionary<string, JsonElement>? Parameters = null);

public sealed record ScheduleScenarioRequest(
    int IntervalSeconds = 60,
    int StartInSeconds = 0,
    Dictionary<string, JsonElement>? Parameters = null);

public sealed record ScenarioView(
    string Id,
    string Name,
    string Summary,
    string BusinessPath,
    string OperationName,
    bool Enabled,
    bool Running,
    ScenarioRunSource? ActiveRunSource,
    DateTimeOffset? ActiveRunStartedAtUtc,
    IReadOnlyDictionary<string, object> Parameters,
    IReadOnlyList<ScenarioParameterDefinition> ParameterDefinitions,
    ScheduleView? Schedule,
    ScenarioRunView? LastRun,
    IncidentDeliveryResult? LastDelivery);

public sealed record ScheduleView(
    bool Enabled,
    int IntervalSeconds,
    DateTimeOffset NextRunAtUtc);

public sealed record ScenarioRunView(
    string RunId,
    string ScenarioId,
    ScenarioRunSource Source,
    ScenarioRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMs,
    string? ErrorType,
    string? ErrorMessage,
    int? HttpStatusCode);

public sealed record ActiveScenarioRun(
    string ScenarioId,
    string RunId,
    ScenarioRunSource Source,
    string BusinessPath,
    string OperationName,
    ScenarioSignalContract Signal,
    IReadOnlyDictionary<string, object> Parameters,
    DateTimeOffset StartedAtUtc,
    CancellationToken CancellationToken);

public sealed record ScenarioRunOutcome(
    string ScenarioId,
    string? RunId,
    ScenarioRunStatus Status,
    int HttpStatusCode,
    object Body);

public sealed record BusinessOkResponse(
    bool Ok,
    string OperationName,
    string Message,
    DateTimeOffset ObservedAtUtc);

public sealed record BusinessFaultResponse(
    bool Ok,
    string ScenarioId,
    string RunId,
    string ErrorType,
    string ErrorMessage,
    int HttpStatusCode,
    bool IncidentDeliveryAttempted,
    bool IncidentDelivered,
    string? IncidentDeliveryError);

