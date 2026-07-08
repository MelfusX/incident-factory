using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Telemetry;

namespace IncidentFactory.Api.Scenarios;

public sealed class ScenarioRunner(
    ScenarioState state,
    IIncidentCompassClient incidentCompassClient,
    IncidentCompassOptions options,
    ILogger<ScenarioRunner> logger)
{
    public async Task<ScenarioRunOutcome> RunAsync(
        string scenarioId,
        ScenarioRunSource source,
        CancellationToken cancellationToken)
    {
        if (!state.TryBeginRun(scenarioId, source, cancellationToken, out var activeRun) || activeRun is null)
        {
            return new ScenarioRunOutcome(
                scenarioId,
                null,
                ScenarioRunStatus.Skipped,
                StatusCodes.Status409Conflict,
                new { ok = false, scenarioId, message = "Scenario is already running or does not exist." });
        }

        var stopwatch = Stopwatch.StartNew();
        var telemetryRun = ScenarioTelemetry.StartRun(activeRun);

        try
        {
            await ExecuteScenarioFaultAsync(activeRun);

            var noFault = new InvalidOperationException("Scenario completed without producing a fault.");
            return await CompleteFaultAsync(
                activeRun,
                stopwatch,
                telemetryRun,
                noFault,
                StatusCodes.Status500InternalServerError);
        }
        catch (OperationCanceledException) when (activeRun.CancellationToken.IsCancellationRequested)
        {
            return CompleteCanceled(activeRun, stopwatch, telemetryRun);
        }
        catch (Exception ex)
        {
            return await CompleteFaultAsync(activeRun, stopwatch, telemetryRun, ex, activeRun.Signal.HttpStatusCode);
        }
    }

    private static async Task ExecuteScenarioFaultAsync(ActiveScenarioRun run)
    {
        switch (run.ScenarioId)
        {
            case ScenarioIds.LatencySpike:
                var latencyMs = GetInt(run.Parameters, "latencyMs", 2500);
                await Task.Delay(latencyMs, run.CancellationToken);
                throw new TimeoutException($"checkout timed out after {latencyMs}ms");

            case ScenarioIds.UnhandledException:
                ThrowPaymentAuthorizationException(GetString(
                    run.Parameters,
                    "exceptionMessage",
                    "Payment authorization state was invalid."));
                return;

            case ScenarioIds.NullReference:
                ThrowCustomerProfileNullReference(GetString(run.Parameters, "customerId", "cust-missing-404"));
                return;

            case ScenarioIds.SerializationError:
                ThrowSerializationError(GetString(run.Parameters, "rawOrderJson", "{\"orderId\":"));
                return;

            case ScenarioIds.RetryStorm:
                await ThrowRetryStormAsync(
                    GetInt(run.Parameters, "attempts", 5),
                    GetInt(run.Parameters, "retryDelayMs", 200),
                    run.CancellationToken);
                return;

            case ScenarioIds.BrokenDownstreamCall:
                throw new HttpRequestException(
                    $"Fulfillment dispatch could not reach {GetString(run.Parameters, "downstreamHost", "http://shipping-gateway.local")}.");

            case ScenarioIds.SimulatedDbTimeout:
                var timeoutMs = GetInt(run.Parameters, "timeoutMs", 3000);
                await Task.Delay(timeoutMs, run.CancellationToken);
                throw new TimeoutException($"reporting query exceeded {timeoutMs}ms");

            case ScenarioIds.BadConfigMisleadingSymptom:
                ThrowMisleadingConfigurationSymptom(GetString(run.Parameters, "configKey", "recommendations.primaryFormatter"));
                return;

            default:
                throw new InvalidOperationException($"Unknown scenario '{run.ScenarioId}'.");
        }
    }

    private async Task<ScenarioRunOutcome> CompleteFaultAsync(
        ActiveScenarioRun run,
        Stopwatch stopwatch,
        ScenarioTelemetry.ScenarioTelemetryRun telemetryRun,
        Exception exception,
        int httpStatusCode)
    {
        stopwatch.Stop();
        var completedAtUtc = DateTimeOffset.UtcNow;
        var errorType = exception.GetType().Name;
        var durationMs = Math.Max(1, stopwatch.ElapsedMilliseconds);

        telemetryRun.RecordFault(exception, httpStatusCode, durationMs);
        telemetryRun.Dispose();

        IncidentDeliveryResult? delivery;
        try
        {
            delivery = await incidentCompassClient.SendAsync(
                BuildEnvelope(run, exception, errorType, httpStatusCode, durationMs, completedAtUtc),
                run.CancellationToken);
        }
        catch (OperationCanceledException) when (run.CancellationToken.IsCancellationRequested)
        {
            return CompleteCanceled(run, stopwatch, telemetryRun);
        }

        var runView = new ScenarioRunView(
            run.RunId,
            run.ScenarioId,
            run.Source,
            ScenarioRunStatus.Faulted,
            run.StartedAtUtc,
            completedAtUtc,
            durationMs,
            errorType,
            exception.Message,
            httpStatusCode);

        state.CompleteRun(run.ScenarioId, run.RunId, runView, delivery);
        logger.LogWarning(
            "Scenario {ScenarioId} produced {ErrorType}; delivery success={DeliverySuccess}",
            run.ScenarioId,
            errorType,
            delivery.Success);

        return new ScenarioRunOutcome(
            run.ScenarioId,
            run.RunId,
            ScenarioRunStatus.Faulted,
            httpStatusCode,
            new BusinessFaultResponse(
                false,
                run.ScenarioId,
                run.RunId,
                errorType,
                exception.Message,
                httpStatusCode,
                delivery.Attempted,
                delivery.Success,
                delivery.Error));
    }

    private ScenarioRunOutcome CompleteCanceled(
        ActiveScenarioRun run,
        Stopwatch stopwatch,
        ScenarioTelemetry.ScenarioTelemetryRun telemetryRun)
    {
        stopwatch.Stop();
        var completedAtUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
        telemetryRun.RecordCanceled(durationMs);
        telemetryRun.Dispose();
        var runView = new ScenarioRunView(
            run.RunId,
            run.ScenarioId,
            run.Source,
            ScenarioRunStatus.Canceled,
            run.StartedAtUtc,
            completedAtUtc,
            durationMs,
            "OperationCanceledException",
            "Scenario run was canceled.",
            null);

        state.CompleteRun(run.ScenarioId, run.RunId, runView, null);

        return new ScenarioRunOutcome(
            run.ScenarioId,
            run.RunId,
            ScenarioRunStatus.Canceled,
            StatusCodes.Status499ClientClosedRequest,
            new { ok = false, run.ScenarioId, run.RunId, message = "Scenario run was canceled." });
    }

    private IncidentEnvelopeRequest BuildEnvelope(
        ActiveScenarioRun run,
        Exception exception,
        string errorType,
        int httpStatusCode,
        long durationMs,
        DateTimeOffset observedAtUtc)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["errorType"] = errorType,
            ["errorMessage"] = exception.Message,
            ["operationName"] = run.OperationName,
            ["httpMethod"] = "POST",
            ["httpRoute"] = run.BusinessPath,
            ["httpStatusCode"] = httpStatusCode,
            ["durationMs"] = durationMs,
            ["scenarioId"] = run.ScenarioId,
            ["runSource"] = run.Source.ToString()
        };

        return new IncidentEnvelopeRequest
        {
            SourceKind = IncidentCompassOptions.SourceKind,
            ServiceName = options.ServiceName,
            Environment = options.Environment,
            Severity = "error",
            Summary = $"{run.ScenarioId} produced {errorType}",
            Description = exception.Message,
            ObservedAtUtc = observedAtUtc,
            Correlation = new IncidentCorrelation(null, null, run.RunId),
            Attributes = attributes,
            Payload = new Dictionary<string, object?>
            {
                ["scenarioId"] = run.ScenarioId,
                ["runId"] = run.RunId,
                ["source"] = run.Source.ToString(),
                ["parameters"] = run.Parameters
            }
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, object> parameters, string name, int fallback)
    {
        return parameters.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt32(value)
            : fallback;
    }

    private static string GetString(IReadOnlyDictionary<string, object> parameters, string name, string fallback)
    {
        return parameters.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value) ?? fallback
            : fallback;
    }

    private static void ThrowPaymentAuthorizationException(string message)
    {
        throw new InvalidOperationException(message);
    }

    private static void ThrowCustomerProfileNullReference(string customerId)
    {
        CustomerProfile? profile = null;
        _ = customerId;
        _ = profile!.DisplayName.Length;
    }

    private static void ThrowSerializationError(string rawOrderJson)
    {
        try
        {
            _ = JsonSerializer.Deserialize<OrderImportDocument>(rawOrderJson);
        }
        catch (JsonException)
        {
            throw;
        }

        throw new JsonException("Order import payload omitted required line item fields.");
    }

    private static async Task ThrowRetryStormAsync(int attempts, int retryDelayMs, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (retryDelayMs > 0)
            {
                await Task.Delay(retryDelayMs, cancellationToken);
            }
        }

        throw new HttpRequestException($"Inventory reservation failed after {attempts} retry attempts.");
    }

    private static void ThrowMisleadingConfigurationSymptom(string configKey)
    {
        var settings = LoadRecommendationSettingsWithoutValidation(configKey);
        var formatter = settings.Formatter;
        _ = formatter!.Length;
    }

    private static RecommendationSettings LoadRecommendationSettingsWithoutValidation(string configKey)
    {
        _ = configKey;
        return new RecommendationSettings(null);
    }

    private sealed record CustomerProfile(string DisplayName);

    private sealed record OrderImportDocument(string OrderId);

    private sealed record RecommendationSettings(string? Formatter);
}




