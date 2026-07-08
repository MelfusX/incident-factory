namespace IncidentFactory.HardScenarios;

public sealed record HardScenarioDescriptor(
    string Id,
    string Name,
    string Summary,
    string OperationName);

public static class HardScenarioIds
{
    public const string InvoiceReconciliation = "invoice-reconciliation";
    public const string SubscriptionActivation = "subscription-activation";
    public const string ReturnAuthorization = "return-authorization";
}

public static class HardScenarioCatalog
{
    private static readonly HardScenarioDescriptor[] Scenarios =
    [
        new(
            HardScenarioIds.InvoiceReconciliation,
            "Invoice reconciliation",
            "Reconciles a two-company invoice batch and posts ledger entries through policy checks.",
            "finance.invoices.reconcile"),
        new(
            HardScenarioIds.SubscriptionActivation,
            "Subscription activation",
            "Activates a customer subscription by projecting entitlements from a plan payload.",
            "subscriptions.activate"),
        new(
            HardScenarioIds.ReturnAuthorization,
            "Return authorization",
            "Builds return authorizations and restock instructions for sequential service requests.",
            "returns.authorize")
    ];

    public static IReadOnlyList<HardScenarioDescriptor> List()
    {
        return Scenarios;
    }

    public static bool TryGet(string id, out HardScenarioDescriptor? descriptor)
    {
        descriptor = Scenarios.FirstOrDefault(
            scenario => string.Equals(scenario.Id, id, StringComparison.OrdinalIgnoreCase));
        return descriptor is not null;
    }

    public static Task<object> RunAsync(string id, CancellationToken cancellationToken = default)
    {
        return id.ToLowerInvariant() switch
        {
            HardScenarioIds.InvoiceReconciliation => Box(InvoiceReconciliationScenario.RunAsync(cancellationToken)),
            HardScenarioIds.SubscriptionActivation => Box(SubscriptionActivationScenario.RunAsync(cancellationToken)),
            HardScenarioIds.ReturnAuthorization => Box(ReturnAuthorizationScenario.RunAsync(cancellationToken)),
            _ => throw new ArgumentException($"Unknown hard scenario '{id}'.", nameof(id))
        };
    }

    private static async Task<object> Box<T>(Task<T> task)
    {
        return (await task)!;
    }
}

