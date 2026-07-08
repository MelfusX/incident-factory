using System.Diagnostics;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Scenarios;
using IncidentFactory.Api.Telemetry;
using IncidentFactory.HardScenarios;

namespace IncidentFactory.Api.HardScenarios;

public sealed class HardScenarioRunner(
    IIncidentCompassClient incidentCompassClient,
    IncidentCompassOptions options,
    ILogger<HardScenarioRunner> logger)
{
    // Tier-2 status is derived per-exception (GetHttpStatusCode); this contract only satisfies the
    // shared ActiveScenarioRun/telemetry context and is never read for hard scenarios.
    private static readonly ScenarioSignalContract TelemetryOnlySignal =
        new("Exception", StatusCodes.Status500InternalServerError, true);

    public async Task<HardScenarioRunOutcome> RunAsync(string scenarioId, CancellationToken cancellationToken)
    {
        if (!HardScenarioCatalog.TryGet(scenarioId, out var descriptor) || descriptor is null)
        {
            return new HardScenarioRunOutcome(
                scenarioId,
                null,
                HardScenarioRunStatus.NotFound,
                StatusCodes.Status404NotFound,
                new { ok = false, scenarioId, message = "Hard scenario does not exist." });
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = $"{scenarioId}-{startedAtUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var businessPath = HardScenarioEndpointExtensions.GetBusinessPath(descriptor.Id);
        var activeRun = new ActiveScenarioRun(
            descriptor.Id,
            runId,
            ScenarioRunSource.Manual,
            businessPath,
            descriptor.OperationName,
            TelemetryOnlySignal,
            new Dictionary<string, object>(),
            startedAtUtc,
            cancellationToken);
        var telemetryRun = ScenarioTelemetry.StartRun(activeRun);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await HardScenarioCatalog.RunAsync(descriptor.Id, cancellationToken);
            stopwatch.Stop();
            telemetryRun.RecordCompleted(StatusCodes.Status200OK, Math.Max(1, stopwatch.ElapsedMilliseconds));
            telemetryRun.Dispose();

            return new HardScenarioRunOutcome(
                descriptor.Id,
                runId,
                HardScenarioRunStatus.Completed,
                StatusCodes.Status200OK,
                new HardScenarioSuccessResponse(
                    true,
                    descriptor.Id,
                    runId,
                    descriptor.OperationName,
                    HardScenarioEndpointExtensions.TierName,
                    result,
                    DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CompleteCanceled(activeRun, descriptor, stopwatch, telemetryRun);
        }
        catch (Exception ex)
        {
            return await CompleteFaultAsync(activeRun, descriptor, stopwatch, telemetryRun, ex, cancellationToken);
        }
    }

    private async Task<HardScenarioRunOutcome> CompleteFaultAsync(
        ActiveScenarioRun run,
        HardScenarioDescriptor descriptor,
        Stopwatch stopwatch,
        ScenarioTelemetry.ScenarioTelemetryRun telemetryRun,
        Exception exception,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        var completedAtUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(1, stopwatch.ElapsedMilliseconds);
        var errorType = exception.GetType().Name;
        var httpStatusCode = GetHttpStatusCode(exception);

        telemetryRun.RecordFault(exception, httpStatusCode, durationMs);
        telemetryRun.Dispose();

        IncidentDeliveryResult delivery;
        try
        {
            delivery = await incidentCompassClient.SendAsync(
                BuildEnvelope(run, exception, errorType, httpStatusCode, durationMs, completedAtUtc),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CompleteCanceled(run, descriptor, stopwatch, telemetryRun);
        }

        logger.LogWarning(
            "Hard scenario {ScenarioId} produced {ErrorType}; delivery success={DeliverySuccess}",
            run.ScenarioId,
            errorType,
            delivery.Success);

        return new HardScenarioRunOutcome(
            run.ScenarioId,
            run.RunId,
            HardScenarioRunStatus.Faulted,
            httpStatusCode,
            new HardScenarioFaultResponse(
                false,
                run.ScenarioId,
                run.RunId,
                descriptor.OperationName,
                HardScenarioEndpointExtensions.TierName,
                errorType,
                exception.Message,
                httpStatusCode,
                delivery.Attempted,
                delivery.Success,
                delivery.Error));
    }

    private static HardScenarioRunOutcome CompleteCanceled(
        ActiveScenarioRun run,
        HardScenarioDescriptor descriptor,
        Stopwatch stopwatch,
        ScenarioTelemetry.ScenarioTelemetryRun telemetryRun)
    {
        stopwatch.Stop();
        var durationMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
        telemetryRun.RecordCanceled(durationMs);
        telemetryRun.Dispose();

        return new HardScenarioRunOutcome(
            run.ScenarioId,
            run.RunId,
            HardScenarioRunStatus.Canceled,
            StatusCodes.Status499ClientClosedRequest,
            new HardScenarioCanceledResponse(
                false,
                run.ScenarioId,
                run.RunId,
                descriptor.OperationName,
                HardScenarioEndpointExtensions.TierName,
                "Hard scenario run was canceled."));
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
            ["runSource"] = run.Source.ToString(),
            ["tier"] = HardScenarioEndpointExtensions.TierName
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
                ["tier"] = HardScenarioEndpointExtensions.TierName
            }
        };
    }

    private static int GetHttpStatusCode(Exception exception)
    {
        return exception switch
        {
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            ArgumentException => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
