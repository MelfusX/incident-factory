using System.Diagnostics;
using System.Diagnostics.Metrics;
using IncidentFactory.Api.Scenarios;

namespace IncidentFactory.Api.Telemetry;

public static class ScenarioTelemetry
{
    public const string ActivitySourceName = "IncidentFactory.Api.Scenarios";
    public const string MeterName = "IncidentFactory.Api.Scenarios";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ScenarioRuns = Meter.CreateCounter<long>("incident_factory.scenario.runs");
    private static readonly Histogram<double> ScenarioDurationMs = Meter.CreateHistogram<double>(
        "incident_factory.scenario.duration",
        "ms");

    public static ScenarioTelemetryRun StartRun(ActiveScenarioRun run)
    {
        var activity = ActivitySource.StartActivity(run.OperationName, ActivityKind.Internal);
        return new ScenarioTelemetryRun(activity, run);
    }

    public sealed class ScenarioTelemetryRun : IDisposable
    {
        private readonly Activity? _activity;
        private readonly string _scenarioId;
        private readonly string _operationName;
        private readonly string _httpRoute;
        private readonly string _runSource;
        private bool _recordedMetrics;

        internal ScenarioTelemetryRun(Activity? activity, ActiveScenarioRun run)
        {
            _activity = activity;
            _scenarioId = run.ScenarioId;
            _operationName = run.OperationName;
            _httpRoute = run.BusinessPath;
            _runSource = run.Source.ToString();

            _activity?.SetTag("scenarioId", _scenarioId);
            _activity?.SetTag("operationName", _operationName);
            _activity?.SetTag("httpMethod", "POST");
            _activity?.SetTag("httpRoute", _httpRoute);
            _activity?.SetTag("runSource", _runSource);
        }

        public void RecordFault(Exception exception, int httpStatusCode, long durationMs)
        {
            var errorType = exception.GetType().Name;
            SetCompletionTags(errorType, httpStatusCode, durationMs);
            _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity?.AddEvent(new ActivityEvent(
                "exception",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"] = exception.GetType().FullName ?? errorType,
                    ["exception.message"] = exception.Message
                }));

            RecordMetrics("faulted", errorType, httpStatusCode, durationMs);
        }

        public void RecordCanceled(long durationMs)
        {
            SetCompletionTags("OperationCanceledException", StatusCodes.Status499ClientClosedRequest, durationMs);
            RecordMetrics(
                "canceled",
                "OperationCanceledException",
                StatusCodes.Status499ClientClosedRequest,
                durationMs);
        }

        public void RecordCompleted(int httpStatusCode, long durationMs)
        {
            SetCompletionTags("none", httpStatusCode, durationMs);
            _activity?.SetStatus(ActivityStatusCode.Ok);
            RecordMetrics("completed", "none", httpStatusCode, durationMs);
        }

        public void Dispose()
        {
            _activity?.Dispose();
        }

        private void SetCompletionTags(string errorType, int httpStatusCode, long durationMs)
        {
            _activity?.SetTag("errorType", errorType);
            _activity?.SetTag("httpStatusCode", httpStatusCode);
            _activity?.SetTag("durationMs", durationMs);
        }

        private void RecordMetrics(string status, string errorType, int httpStatusCode, long durationMs)
        {
            if (_recordedMetrics)
            {
                return;
            }

            _recordedMetrics = true;
            var tags = new TagList
            {
                { "scenarioId", _scenarioId },
                { "operationName", _operationName },
                { "httpRoute", _httpRoute },
                { "httpStatusCode", httpStatusCode },
                { "errorType", errorType },
                { "runSource", _runSource },
                { "scenarioStatus", status }
            };

            ScenarioRuns.Add(1, tags);
            ScenarioDurationMs.Record(durationMs, tags);
        }
    }
}

