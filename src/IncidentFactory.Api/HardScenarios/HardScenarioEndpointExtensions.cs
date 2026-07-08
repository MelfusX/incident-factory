using IncidentFactory.HardScenarios;

namespace IncidentFactory.Api.HardScenarios;

public static class HardScenarioEndpointExtensions
{
    public const string TierName = "tier-2";

    public static IEndpointRouteBuilder MapHardScenarioBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hard").WithTags("Hard Scenarios");

        group.MapGet("", () => Results.Ok(GetHardScenarioViews()));

        group.MapPost("/{id}", async (
            string id,
            HardScenarioRunner runner,
            CancellationToken cancellationToken) =>
        {
            var outcome = await runner.RunAsync(id, cancellationToken);
            return Results.Json(outcome.Body, statusCode: outcome.HttpStatusCode);
        });

        return app;
    }

    public static IReadOnlyList<HardScenarioView> GetHardScenarioViews()
    {
        return HardScenarioCatalog.List()
            .Select(scenario => new HardScenarioView(
                scenario.Id,
                scenario.Name,
                scenario.Summary,
                GetBusinessPath(scenario.Id),
                scenario.OperationName,
                TierName))
            .ToArray();
    }

    public static string GetBusinessPath(string scenarioId)
    {
        return $"/api/hard/{scenarioId}";
    }
}
