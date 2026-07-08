namespace IncidentFactory.Api.Scenarios;

public static class ScenarioEndpointExtensions
{
    public static IEndpointRouteBuilder MapScenarioControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scenarios").WithTags("Scenarios");

        group.MapGet("", (ScenarioState state) => Results.Ok(state.GetAllViews()));

        group.MapPost("/reset-all", (ScenarioState state) => Results.Ok(state.ResetAll()));

        group.MapGet("/{id}", (string id, ScenarioState state) =>
            state.TryGetView(id, out var view) ? Results.Ok(view) : Results.NotFound());

        group.MapPost("/{id}/trigger", (string id, ScenarioTriggerRequest? request, ScenarioState state) =>
            state.Enable(id, request?.Parameters, out var view) ? Results.Ok(view) : Results.NotFound());

        group.MapPost("/{id}/disable", (string id, ScenarioState state) =>
            state.Disable(id, out var view) ? Results.Ok(view) : Results.NotFound());

        group.MapPost("/{id}/schedule", (string id, ScheduleScenarioRequest? request, ScenarioState state) =>
        {
            var scheduleRequest = request ?? new ScheduleScenarioRequest();
            var scheduled = state.Schedule(id, scheduleRequest, out var view, out var validationError);

            if (scheduled)
            {
                return Results.Ok(view);
            }

            return validationError is not null
                ? Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest)
                : Results.NotFound();
        });

        group.MapDelete("/{id}/schedule", (string id, ScenarioState state) =>
            state.CancelSchedule(id, out var view) ? Results.Ok(view) : Results.NotFound());

        return app;
    }

    public static IEndpointRouteBuilder MapScenarioBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        var catalog = app.ServiceProvider.GetRequiredService<ScenarioCatalog>();

        foreach (var scenario in catalog.Scenarios)
        {
            var scenarioId = scenario.Id;
            var operationName = scenario.OperationName;
            var businessPath = scenario.BusinessPath;

            app.MapPost(businessPath, async (
                ScenarioState state,
                ScenarioRunner runner,
                CancellationToken cancellationToken) =>
            {
                if (!state.IsEnabled(scenarioId))
                {
                    return Results.Ok(new BusinessOkResponse(
                        true,
                        operationName,
                        "No active scenario for this operation.",
                        DateTimeOffset.UtcNow));
                }

                var outcome = await runner.RunAsync(scenarioId, ScenarioRunSource.Manual, cancellationToken);
                return Results.Json(outcome.Body, statusCode: outcome.HttpStatusCode);
            }).WithName(ToEndpointName(scenarioId));
        }

        return app;
    }

    private static string ToEndpointName(string scenarioId)
    {
        return string.Concat(
            scenarioId.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
