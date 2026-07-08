using System.Text.Json;
using IncidentFactory.Api.Scenarios;
using Xunit;

namespace IncidentFactory.Tests;

public class ScenarioStateTests
{
    [Fact]
    public void ResetClearsFlagsAndSchedules()
    {
        var state = NewState();

        Assert.True(state.Enable(ScenarioIds.LatencySpike, JsonParams("{\"latencyMs\":1234}"), out var enabled));
        Assert.NotNull(enabled);
        Assert.True(enabled!.Enabled);

        Assert.True(
            state.Schedule(ScenarioIds.UnhandledException, new ScheduleScenarioRequest(5, 0), out var scheduled, out var error),
            error ?? "exception scenario scheduled");
        Assert.NotNull(scheduled!.Schedule);

        var views = state.ResetAll();

        Assert.All(views, view => Assert.False(view.Enabled));
        Assert.All(views, view => Assert.Null(view.Schedule));
    }

    [Fact]
    public void ScenarioTogglesAreIsolated()
    {
        var state = NewState();

        Assert.True(state.Enable(ScenarioIds.LatencySpike, JsonParams("{\"latencyMs\":750}"), out _));
        Assert.True(state.TryGetView(ScenarioIds.LatencySpike, out var latency));
        Assert.True(state.TryGetView(ScenarioIds.UnhandledException, out var exception));

        Assert.True(latency!.Enabled);
        Assert.False(exception!.Enabled);
        Assert.Equal(750, Convert.ToInt32(latency.Parameters["latencyMs"]));
    }

    [Fact]
    public void RunningScenariosAreNonOverlappingAndCancelable()
    {
        var state = NewState();

        Assert.True(state.TryBeginRun(ScenarioIds.LatencySpike, ScenarioRunSource.Manual, CancellationToken.None, out var first));
        Assert.False(state.TryBeginRun(ScenarioIds.LatencySpike, ScenarioRunSource.Manual, CancellationToken.None, out _));

        state.ResetAll();

        Assert.NotNull(first);
        Assert.True(first!.CancellationToken.IsCancellationRequested);

        state.CompleteRun(
            first.ScenarioId,
            first.RunId,
            new ScenarioRunView(
                first.RunId,
                first.ScenarioId,
                first.Source,
                ScenarioRunStatus.Canceled,
                first.StartedAtUtc,
                DateTimeOffset.UtcNow,
                0,
                "OperationCanceledException",
                "Scenario run was canceled.",
                null),
            null);

        Assert.True(state.TryBeginRun(ScenarioIds.LatencySpike, ScenarioRunSource.Manual, CancellationToken.None, out _));
    }

    [Fact]
    public void DueSchedulesAreTakenOncePerInterval()
    {
        var state = NewState();

        Assert.True(
            state.Schedule(ScenarioIds.LatencySpike, new ScheduleScenarioRequest(30, 0), out _, out var error),
            error ?? "scenario scheduled");

        var firstDue = state.TakeDueScheduledRuns(DateTimeOffset.UtcNow);
        var secondDue = state.TakeDueScheduledRuns(DateTimeOffset.UtcNow);

        Assert.Equal(new[] { ScenarioIds.LatencySpike }, firstDue);
        Assert.Empty(secondDue);
    }

    private static ScenarioState NewState() => new(ScenarioCatalog.Create());

    private static Dictionary<string, JsonElement> JsonParams(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
