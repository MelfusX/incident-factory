namespace IncidentFactory.Api.Scenarios;

public sealed class ScheduledScenarioWorker(
    ScenarioState state,
    ScenarioRunner runner,
    ILogger<ScheduledScenarioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dueScenarioIds = state.TakeDueScheduledRuns(DateTimeOffset.UtcNow);

                foreach (var scenarioId in dueScenarioIds)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var outcome = await runner.RunAsync(scenarioId, ScenarioRunSource.Schedule, stoppingToken);
                    logger.LogInformation(
                        "Scheduled scenario {ScenarioId} completed with status {Status}.",
                        scenarioId,
                        outcome.Status);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled scenario worker failed a polling iteration.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
