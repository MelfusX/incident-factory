using IncidentFactory.Api.Scenarios;

namespace IncidentFactory.Api.IncidentCompass;

public static class IncidentCompassReportEndpointExtensions
{
    public static IEndpointRouteBuilder MapIncidentCompassReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("IncidentCompass Reports");

        group.MapGet("/faults/{faultId}", async (
            string faultId,
            IIncidentCompassReportClient reportClient,
            CancellationToken cancellationToken) =>
        {
            return await GetReportForFaultAsync(faultId, reportClient, cancellationToken);
        });

        return app;
    }

    public static IEndpointRouteBuilder MapScenarioReportEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scenarios/{id}/report", async (
            string id,
            ScenarioState state,
            IIncidentCompassReportClient reportClient,
            CancellationToken cancellationToken) =>
        {
            if (!state.TryGetLastFaultId(id, out var faultId))
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(faultId))
            {
                return Results.Ok(new IncidentCompassReportLookupResponse(
                    IncidentCompassReportLookupStatus.NoFault,
                    null,
                    null,
                    "This scenario does not have a delivered IncidentCompass fault yet.",
                    null));
            }

            return await GetReportForFaultAsync(faultId, reportClient, cancellationToken);
        })
        .WithTags("IncidentCompass Reports");

        return app;
    }

    private static async Task<IResult> GetReportForFaultAsync(
        string faultId,
        IIncidentCompassReportClient reportClient,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await reportClient.GetLatestReportForFaultAsync(faultId, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (IncidentCompassReportReadException ex)
        {
            return Results.Problem(
                ex.ResponsePreview is null
                    ? ex.Message
                    : $"{ex.Message} Response preview: {ex.ResponsePreview}",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
