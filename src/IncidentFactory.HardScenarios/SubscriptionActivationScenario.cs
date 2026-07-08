namespace IncidentFactory.HardScenarios;

public sealed record SubscriptionActivationResult(
    string AccountId,
    string PlanCode,
    IReadOnlyDictionary<string, int> Entitlements);

internal static class SubscriptionActivationScenario
{
    public static Task<SubscriptionActivationResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var service = new SubscriptionActivationService(
            new PlanLimitReader(),
            new EntitlementProjector(),
            new EntitlementPolicy());
        var request = new ActivationRequest(
            "acct-4319",
            "care-team-plus",
            75,
            "support:75;notes:;analytics:50;exports:25");

        return Task.FromResult(service.Activate(request));
    }

    private sealed class SubscriptionActivationService(
        PlanLimitReader planLimitReader,
        EntitlementProjector entitlementProjector,
        EntitlementPolicy entitlementPolicy)
    {
        public SubscriptionActivationResult Activate(ActivationRequest request)
        {
            var limits = planLimitReader.Read(request.PlanLimitPayload);
            var entitlements = entitlementProjector.Project(request.SeatCount, limits);
            entitlementPolicy.EnsureAllowed(request, entitlements);

            return new SubscriptionActivationResult(request.AccountId, request.PlanCode, entitlements);
        }
    }

    private sealed class PlanLimitReader
    {
        public IReadOnlyDictionary<string, int> Read(string payload)
        {
            Dictionary<string, int> limits = new(StringComparer.OrdinalIgnoreCase);

            foreach (var segment in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var parts = segment.Split(':', StringSplitOptions.TrimEntries);
                    limits[parts[0]] = int.Parse(parts[1]);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or IndexOutOfRangeException)
                {
                    return limits;
                }
            }

            return limits;
        }
    }

    private sealed class EntitlementProjector
    {
        private static readonly string[] RequiredFeatures = ["support", "analytics", "exports"];

        public IReadOnlyDictionary<string, int> Project(
            int seatCount,
            IReadOnlyDictionary<string, int> limits)
        {
            Dictionary<string, int> entitlements = new(StringComparer.OrdinalIgnoreCase);

            foreach (var feature in RequiredFeatures)
            {
                entitlements[feature] = Math.Min(seatCount, limits.GetValueOrDefault(feature, int.MaxValue));
            }

            return entitlements;
        }
    }

    private sealed class EntitlementPolicy
    {
        private const int AnalyticsCap = 50;

        public void EnsureAllowed(ActivationRequest request, IReadOnlyDictionary<string, int> entitlements)
        {
            if (entitlements.GetValueOrDefault("analytics") > AnalyticsCap)
            {
                throw new InvalidOperationException(
                    $"Subscription activation exceeded entitlement policy for account {request.AccountId}.");
            }
        }
    }

    private sealed record ActivationRequest(
        string AccountId,
        string PlanCode,
        int SeatCount,
        string PlanLimitPayload);
}

