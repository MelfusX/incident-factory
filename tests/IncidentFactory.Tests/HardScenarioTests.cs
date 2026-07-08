using System.Runtime.CompilerServices;
using IncidentFactory.Api.HardScenarios;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Scenarios;
using IncidentFactory.HardScenarios;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IncidentFactory.Tests;

public sealed class HardScenarioTests
{
    [Fact]
    public void HardScenariosProjectRemainsIsolatedFromApi()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            // Source tree not reachable from this run location (e.g. the compiled test assembly was
            // copied outside the repo). The one-way project reference already enforces isolation at
            // compile time, so skip the source scan rather than fail spuriously.
            return;
        }

        var projectDirectory = Path.Combine(root, "src", "IncidentFactory.HardScenarios");
        var projectFile = Path.Combine(projectDirectory, "IncidentFactory.HardScenarios.csproj");

        Assert.DoesNotContain("IncidentFactory.Api", File.ReadAllText(projectFile), StringComparison.OrdinalIgnoreCase);

        foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !IsBuildOutput(file)))
        {
            Assert.DoesNotContain(
                "IncidentFactory.Api",
                File.ReadAllText(sourceFile),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HardScenarioCatalogExposesTier2Views()
    {
        var scenarios = HardScenarioEndpointExtensions.GetHardScenarioViews();

        Assert.Equal(
            [
                HardScenarioIds.InvoiceReconciliation,
                HardScenarioIds.SubscriptionActivation,
                HardScenarioIds.ReturnAuthorization
            ],
            scenarios.Select(scenario => scenario.Id).ToArray());
        Assert.All(scenarios, scenario =>
        {
            Assert.Equal("tier-2", scenario.Tier);
            Assert.StartsWith("/api/hard/", scenario.BusinessPath, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(scenario.Name));
            Assert.False(string.IsNullOrWhiteSpace(scenario.Summary));
            Assert.False(string.IsNullOrWhiteSpace(scenario.OperationName));
        });
    }

    [Fact]
    public async Task HardScenarioRunnerEmitsStableFaultSignals()
    {
        var incidentCompass = new CapturingIncidentCompassClient();
        var runner = new HardScenarioRunner(
            incidentCompass,
            new IncidentCompassOptions(),
            new SilentLogger<HardScenarioRunner>());
        var scenarios = HardScenarioEndpointExtensions.GetHardScenarioViews();

        foreach (var scenario in scenarios)
        {
            var outcome = await runner.RunAsync(scenario.Id, CancellationToken.None);

            Assert.Equal(HardScenarioRunStatus.Faulted, outcome.Status);
            Assert.Equal(StatusCodes.Status500InternalServerError, outcome.HttpStatusCode);
            var body = Assert.IsType<HardScenarioFaultResponse>(outcome.Body);
            Assert.False(body.Ok);
            Assert.Equal(scenario.Id, body.ScenarioId);
            Assert.Equal(scenario.OperationName, body.OperationName);
            Assert.Equal("tier-2", body.Tier);
            Assert.Equal(outcome.HttpStatusCode, body.HttpStatusCode);
            Assert.True(body.IncidentDeliveryAttempted);
            Assert.True(body.IncidentDelivered);
            Assert.False(string.IsNullOrWhiteSpace(body.ErrorType));
            Assert.False(string.IsNullOrWhiteSpace(body.ErrorMessage));
        }

        Assert.Equal(scenarios.Count, incidentCompass.Envelopes.Count);

        foreach (var (scenario, envelope) in scenarios.Zip(incidentCompass.Envelopes))
        {
            Assert.Equal(IncidentCompassOptions.SourceKind, envelope.SourceKind);
            Assert.Equal("incident-factory", envelope.ServiceName);
            Assert.Equal("demo", envelope.Environment);
            Assert.NotNull(envelope.ObservedAtUtc);
            Assert.StartsWith(scenario.Id, envelope.Correlation?.ExternalId, StringComparison.Ordinal);

            var attributes = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(envelope.Attributes);
            Assert.Equal(scenario.OperationName, Convert.ToString(attributes["operationName"]));
            Assert.Equal("POST", Convert.ToString(attributes["httpMethod"]));
            Assert.Equal(scenario.BusinessPath, Convert.ToString(attributes["httpRoute"]));
            Assert.Equal(StatusCodes.Status500InternalServerError, Convert.ToInt32(attributes["httpStatusCode"]));
            Assert.Equal(scenario.Id, Convert.ToString(attributes["scenarioId"]));
            Assert.Equal(ScenarioRunSource.Manual.ToString(), Convert.ToString(attributes["runSource"]));
            Assert.Equal("tier-2", Convert.ToString(attributes["tier"]));
            Assert.True(Convert.ToInt64(attributes["durationMs"]) >= 0);
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
                    "scenarioId",
                    "tier"
                ],
                attributes.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task HardScenarioRunnerDoesNotMutateTier1ControlState()
    {
        var state = new ScenarioState(ScenarioCatalog.Create());
        Assert.True(state.Enable(ScenarioIds.LatencySpike, null, out _));

        var runner = new HardScenarioRunner(
            new CapturingIncidentCompassClient(),
            new IncidentCompassOptions(),
            new SilentLogger<HardScenarioRunner>());

        var outcome = await runner.RunAsync(HardScenarioIds.InvoiceReconciliation, CancellationToken.None);
        Assert.Equal(HardScenarioRunStatus.Faulted, outcome.Status);

        var views = state.ResetAll();
        Assert.All(views, view => Assert.False(view.Enabled));
        Assert.All(views, view => Assert.Null(view.Schedule));
    }

    private static string? FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
    {
        string?[] seeds = [Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory];

        foreach (var seed in seeds)
        {
            for (var directory = seed is null ? null : new DirectoryInfo(seed);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IncidentFactory.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private static bool IsBuildOutput(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
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


