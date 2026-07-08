using System.Text.Json;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Scenarios;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IncidentFactory.Tests;

public class ScenarioSignalOracleTests
{
    [Fact]
    public void Tier1CatalogContainsExpectedScenarios()
    {
        var ids = ScenarioCatalog.Create().Scenarios.Select(scenario => scenario.Id).ToArray();

        Assert.Equal(
            [
                ScenarioIds.LatencySpike,
                ScenarioIds.UnhandledException,
                ScenarioIds.NullReference,
                ScenarioIds.SerializationError,
                ScenarioIds.RetryStorm,
                ScenarioIds.BrokenDownstreamCall,
                ScenarioIds.SimulatedDbTimeout,
                ScenarioIds.BadConfigMisleadingSymptom
            ],
            ids);
    }

    [Theory]
    [MemberData(nameof(ScenarioIdsData))]
    public async Task Tier1ScenariosEmitExpectedSignalContract(string scenarioId)
    {
        var catalog = ScenarioCatalog.Create();
        var scenario = catalog.Scenarios.Single(s => s.Id == scenarioId);
        var state = new ScenarioState(catalog);
        var incidentCompass = new CapturingIncidentCompassClient();
        var runner = new ScenarioRunner(
            state,
            incidentCompass,
            new IncidentCompassOptions(),
            new SilentLogger<ScenarioRunner>());

        Assert.True(state.Enable(scenario.Id, FastParameters(scenario), out _));

        var outcome = await runner.RunAsync(scenario.Id, ScenarioRunSource.Manual, CancellationToken.None);

        Assert.Equal(ScenarioRunStatus.Faulted, outcome.Status);
        Assert.Equal(scenario.Signal.HttpStatusCode, outcome.HttpStatusCode);

        var envelope = Assert.Single(incidentCompass.Envelopes);
        Assert.Equal(IncidentCompassOptions.SourceKind, envelope.SourceKind);
        Assert.Equal("incident-factory", envelope.ServiceName);
        Assert.Equal("demo", envelope.Environment);
        Assert.NotNull(envelope.ObservedAtUtc);
        Assert.Equal(outcome.RunId, envelope.Correlation?.ExternalId);

        var attributes = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(envelope.Attributes);
        Assert.Equal(scenario.Signal.ErrorType, Convert.ToString(attributes["errorType"]));
        Assert.Equal(scenario.OperationName, Convert.ToString(attributes["operationName"]));
        Assert.Equal("POST", Convert.ToString(attributes["httpMethod"]));
        Assert.Equal(scenario.BusinessPath, Convert.ToString(attributes["httpRoute"]));
        Assert.Equal(scenario.Signal.HttpStatusCode, Convert.ToInt32(attributes["httpStatusCode"]));
        Assert.Equal(scenario.Id, Convert.ToString(attributes["scenarioId"]));
        Assert.Equal(ScenarioRunSource.Manual.ToString(), Convert.ToString(attributes["runSource"]));
        Assert.Equal(
            [
                "durationMs",
                "errorMessage",
                "errorType",
                "httpMethod",
                "httpRoute",
                "httpStatusCode",
                "operationName",
                "runSource",
                "scenarioId"
            ],
            attributes.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());

        if (scenario.Signal.RequiresDurationMs)
        {
            Assert.True(attributes.ContainsKey("durationMs"));
            Assert.True(Convert.ToInt64(attributes["durationMs"]) >= 0);
        }

        Assert.True(state.TryGetView(scenario.Id, out var view));
        Assert.Equal(scenario.Signal.ErrorType, view!.LastRun?.ErrorType);
        Assert.Equal(scenario.Signal.HttpStatusCode, view.LastRun?.HttpStatusCode);
        Assert.True(view.LastDelivery?.Attempted);
    }

    public static IEnumerable<object[]> ScenarioIdsData()
    {
        return ScenarioCatalog.Create().Scenarios.Select(scenario => new object[] { scenario.Id });
    }

    private static Dictionary<string, JsonElement> FastParameters(ScenarioDefinition scenario)
    {
        var values = scenario.Parameters.ToDictionary(parameter => parameter.Name, FastValue);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));

        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static object FastValue(ScenarioParameterDefinition parameter)
    {
        if (parameter.Kind != "number")
        {
            return parameter.DefaultValue;
        }

        return parameter.Name switch
        {
            "attempts" => 1,
            "retryDelayMs" => 0,
            _ => parameter.Min ?? Convert.ToInt32(parameter.DefaultValue)
        };
    }

    private sealed class CapturingIncidentCompassClient : IIncidentCompassClient
    {
        public List<IncidentEnvelopeRequest> Envelopes { get; } = [];

        public Task<IncidentDeliveryResult> SendAsync(
            IncidentEnvelopeRequest envelope,
            CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);

            return Task.FromResult(new IncidentDeliveryResult(
                Attempted: true,
                Success: true,
                Url: "http://localhost:5198/api/v1/incidents",
                StatusCode: 201,
                SignalId: "signal-test",
                FaultId: "fault-test",
                JobId: "job-test",
                Error: null,
                ResponsePreview: null,
                DeliveredAtUtc: DateTimeOffset.UtcNow));
        }
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


