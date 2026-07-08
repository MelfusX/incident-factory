namespace IncidentFactory.HardScenarios;

public sealed record ReturnAuthorizationSummary(
    string AuthorizationBatchId,
    int AuthorizedReturns,
    IReadOnlyList<string> RestockDestinations);

internal static class ReturnAuthorizationScenario
{
    public static async Task<ReturnAuthorizationSummary> RunAsync(CancellationToken cancellationToken)
    {
        var service = new ReturnAuthorizationService(
            new ReturnEligibilityService(),
            new RestockPlanner(),
            new RestockPlanVerifier());
        var requests = new[]
        {
            new ReturnRequest("RMA-7841", "sku-monitor-arm", 4, "sellable"),
            new ReturnRequest("RMA-7842", "sku-field-kit", 3, "repair")
        };

        List<RestockPlan> plans = [];
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plans.Add(await service.AuthorizeAsync(request, cancellationToken));
        }

        return new ReturnAuthorizationSummary(
            "returns-2026-07-east",
            plans.Count,
            plans.SelectMany(plan => plan.Lines).Select(line => line.Destination).Distinct().ToArray());
    }

    private sealed class ReturnAuthorizationService(
        ReturnEligibilityService eligibilityService,
        RestockPlanner restockPlanner,
        RestockPlanVerifier restockPlanVerifier)
    {
        public async Task<RestockPlan> AuthorizeAsync(ReturnRequest request, CancellationToken cancellationToken)
        {
            var eligibility = await eligibilityService.CheckAsync(request, cancellationToken);
            var plan = restockPlanner.CreatePlan(request, eligibility);
            restockPlanVerifier.Verify(request, plan);
            return plan;
        }
    }

    private sealed class ReturnEligibilityService
    {
        public Task<ReturnEligibility> CheckAsync(ReturnRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = request.ConditionCode == "sellable" ? "available-stock" : "repair-hold";
            return Task.FromResult(new ReturnEligibility(request.ReturnId, destination, request.Quantity));
        }
    }

    private sealed class RestockPlanner
    {
        private readonly RestockPlan _workingPlan = new();

        public RestockPlan CreatePlan(ReturnRequest request, ReturnEligibility eligibility)
        {
            _workingPlan.Lines.Add(new RestockLine(
                request.ReturnId,
                request.Sku,
                eligibility.Destination,
                eligibility.AcceptedQuantity));

            return _workingPlan;
        }
    }

    private sealed class RestockPlanVerifier
    {
        public void Verify(ReturnRequest request, RestockPlan plan)
        {
            if (plan.Lines.Any(line => line.ReturnId != request.ReturnId))
            {
                throw new InvalidOperationException(
                    $"Return authorization failed final consistency check for {request.ReturnId}.");
            }
        }
    }

    private sealed record ReturnRequest(
        string ReturnId,
        string Sku,
        int Quantity,
        string ConditionCode);

    private sealed record ReturnEligibility(string ReturnId, string Destination, int AcceptedQuantity);

    private sealed class RestockPlan
    {
        public List<RestockLine> Lines { get; } = [];
    }

    private sealed record RestockLine(
        string ReturnId,
        string Sku,
        string Destination,
        int Quantity);
}
