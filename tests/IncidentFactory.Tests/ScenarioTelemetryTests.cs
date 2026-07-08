using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Scenarios;
using IncidentFactory.Api.Telemetry;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IncidentFactory.Tests;

public sealed class ScenarioTelemetryTests
{
    [Fact]
    public async Task BusinessSpanStopsBeforeIncidentCompassDelivery()
    {
        const string faultMessage = "telemetry span delivery boundary";
        List<TimeSpan> durations = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ScenarioTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                var exceptionMessage = activity.Events
                    .SelectMany(activityEvent => activityEvent.Tags)
                    .FirstOrDefault(tag => tag.Key == "exception.message")
                    .Value?.ToString();

                if (exceptionMessage == faultMessage)
                {
                    durations.Add(activity.Duration);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var delay = TimeSpan.FromMilliseconds(800);
        var runner = CreateRunner(new DelayedIncidentCompassClient(delay), out var state);
        Assert.True(state.Enable(
            ScenarioIds.UnhandledException,
            JsonParams($"{{\"exceptionMessage\":\"{faultMessage}\"}}"),
            out _));

        var stopwatch = Stopwatch.StartNew();
        var outcome = await runner.RunAsync(ScenarioIds.UnhandledException, ScenarioRunSource.Manual, CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(ScenarioRunStatus.Faulted, outcome.Status);
        Assert.True(stopwatch.Elapsed >= delay.Subtract(TimeSpan.FromMilliseconds(100)));
        var businessSpanDuration = Assert.Single(durations);
        Assert.True(
            businessSpanDuration < TimeSpan.FromMilliseconds(300),
            $"Business span duration included delivery time: {businessSpanDuration}.");
    }

    [Fact]
    public async Task ScenarioMetricsDoNotUseDurationAsTag()
    {
        var observedMetricTagKeys = new ConcurrentQueue<string[]>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ScenarioTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => observedMetricTagKeys.Enqueue(ReadTagKeys(tags)));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => observedMetricTagKeys.Enqueue(ReadTagKeys(tags)));
        listener.Start();

        var runner = CreateRunner(new CapturingIncidentCompassClient(), out var state);
        Assert.True(state.Enable(
            ScenarioIds.UnhandledException,
            JsonParams("{\"exceptionMessage\":\"Payment authorization state was invalid.\"}"),
            out _));

        var outcome = await runner.RunAsync(ScenarioIds.UnhandledException, ScenarioRunSource.Manual, CancellationToken.None);

        Assert.Equal(ScenarioRunStatus.Faulted, outcome.Status);
        Assert.NotEmpty(observedMetricTagKeys);
        Assert.All(observedMetricTagKeys, tagKeys => Assert.DoesNotContain("durationMs", tagKeys));
        Assert.Contains(observedMetricTagKeys, tagKeys => tagKeys.Contains("scenarioId"));
    }

    [Fact]
    public void CompletedRunRecordsMetricWithoutDurationTag()
    {
        var observedMetricTagKeys = new ConcurrentQueue<string[]>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ScenarioTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => observedMetricTagKeys.Enqueue(ReadTagKeys(tags)));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => observedMetricTagKeys.Enqueue(ReadTagKeys(tags)));
        listener.Start();

        var run = new ActiveScenarioRun(
            "telemetry-complete",
            "run-complete",
            ScenarioRunSource.Manual,
            "/api/hard/telemetry-complete",
            "telemetry.complete",
            new ScenarioSignalContract("Exception", 500, true),
            new Dictionary<string, object>(),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var telemetryRun = ScenarioTelemetry.StartRun(run);
        telemetryRun.RecordCompleted(200, 12);
        telemetryRun.Dispose();

        Assert.NotEmpty(observedMetricTagKeys);
        Assert.All(observedMetricTagKeys, tagKeys => Assert.DoesNotContain("durationMs", tagKeys));
        Assert.Contains(observedMetricTagKeys, tagKeys => tagKeys.Contains("scenarioStatus"));
    }

    private static ScenarioRunner CreateRunner(IIncidentCompassClient incidentCompassClient, out ScenarioState state)
    {
        var catalog = ScenarioCatalog.Create();
        state = new ScenarioState(catalog);
        return new ScenarioRunner(
            state,
            incidentCompassClient,
            new IncidentCompassOptions(),
            new SilentLogger<ScenarioRunner>());
    }

    private static string[] ReadTagKeys(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var keys = new List<string>();
        foreach (var tag in tags)
        {
            keys.Add(tag.Key);
        }

        return keys.ToArray();
    }

    private static Dictionary<string, JsonElement> JsonParams(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private sealed class CapturingIncidentCompassClient : IIncidentCompassClient
    {
        public Task<IncidentDeliveryResult> SendAsync(
            IncidentEnvelopeRequest envelope,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateDeliveryResult());
        }
    }

    private sealed class DelayedIncidentCompassClient(TimeSpan delay) : IIncidentCompassClient
    {
        public async Task<IncidentDeliveryResult> SendAsync(
            IncidentEnvelopeRequest envelope,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return CreateDeliveryResult();
        }
    }

    private static IncidentDeliveryResult CreateDeliveryResult()
    {
        return new IncidentDeliveryResult(
            Attempted: true,
            Success: true,
            Url: "http://localhost:5198/api/v1/incidents",
            StatusCode: 201,
            SignalId: "signal-test",
            FaultId: "fault-test",
            JobId: "job-test",
            Error: null,
            ResponsePreview: null,
            DeliveredAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

