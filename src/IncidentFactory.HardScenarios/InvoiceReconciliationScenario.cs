namespace IncidentFactory.HardScenarios;

public sealed record InvoiceReconciliationReceipt(
    string BatchId,
    int PostedInvoices,
    decimal PostedTotal,
    IReadOnlyList<string> PostingIds);

internal static class InvoiceReconciliationScenario
{
    public static Task<InvoiceReconciliationReceipt> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var vendorDirectory = new VendorDirectory(
        [
            new VendorProfile("northwind-retail", "lab-1024", "LabSource Retail", "expense-clearing"),
            new VendorProfile("northwind-health", "lab-1024", "LabSource Clinical", "clinical-supplies-restricted")
        ]);
        var service = new InvoiceReconciliationService(vendorDirectory, new LedgerPostingService());
        var batch = new InvoiceBatch(
            "batch-2026-07-clinical",
            [
                new InvoiceDocument("northwind-retail", "lab-1024", "INV-88410", 4200m, "expense-clearing"),
                new InvoiceDocument("northwind-health", "lab-1024", "INV-88411", 16800m, "clinical-supplies-restricted")
            ]);

        return Task.FromResult(service.Reconcile(batch));
    }

    private sealed class InvoiceReconciliationService(
        VendorDirectory vendorDirectory,
        LedgerPostingService ledgerPostingService)
    {
        public InvoiceReconciliationReceipt Reconcile(InvoiceBatch batch)
        {
            List<LedgerPostingReceipt> receipts = [];

            foreach (var invoice in batch.Documents)
            {
                var profile = vendorDirectory.Resolve(invoice.CompanyCode, invoice.VendorCode);
                receipts.Add(ledgerPostingService.Post(invoice, profile));
            }

            return new InvoiceReconciliationReceipt(
                batch.BatchId,
                receipts.Count,
                receipts.Sum(receipt => receipt.Amount),
                receipts.Select(receipt => receipt.PostingId).ToArray());
        }
    }

    private sealed class VendorDirectory(IEnumerable<VendorProfile> profiles)
    {
        private readonly Dictionary<string, VendorProfile> _profiles = profiles.ToDictionary(
            profile => CreateLookupKey(profile.CompanyCode, profile.VendorCode),
            StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VendorProfile> _cache = new(StringComparer.OrdinalIgnoreCase);

        public VendorProfile Resolve(string companyCode, string vendorCode)
        {
            if (_cache.TryGetValue(vendorCode, out var cached))
            {
                return cached;
            }

            var profile = _profiles[CreateLookupKey(companyCode, vendorCode)];
            _cache[vendorCode] = profile;
            return profile;
        }

        private static string CreateLookupKey(string companyCode, string vendorCode)
        {
            return $"{companyCode}:{vendorCode}";
        }
    }

    private sealed class LedgerPostingService
    {
        public LedgerPostingReceipt Post(InvoiceDocument invoice, VendorProfile profile)
        {
            var posting = new LedgerPosting(
                $"post-{invoice.InvoiceId}",
                invoice.CompanyCode,
                invoice.InvoiceId,
                profile.DisplayName,
                profile.DefaultLedgerAccount,
                invoice.Amount);

            LedgerPolicy.EnsureAccepted(invoice, posting);
            return new LedgerPostingReceipt(posting.PostingId, posting.Amount);
        }
    }

    private static class LedgerPolicy
    {
        public static void EnsureAccepted(InvoiceDocument invoice, LedgerPosting posting)
        {
            if (!string.Equals(invoice.RequiredLedgerAccount, posting.LedgerAccount, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Invoice {invoice.InvoiceId} was rejected by ledger policy for company {invoice.CompanyCode}.");
            }
        }
    }

    private sealed record InvoiceBatch(string BatchId, IReadOnlyList<InvoiceDocument> Documents);

    private sealed record InvoiceDocument(
        string CompanyCode,
        string VendorCode,
        string InvoiceId,
        decimal Amount,
        string RequiredLedgerAccount);

    private sealed record VendorProfile(
        string CompanyCode,
        string VendorCode,
        string DisplayName,
        string DefaultLedgerAccount);

    private sealed record LedgerPosting(
        string PostingId,
        string CompanyCode,
        string InvoiceId,
        string VendorDisplayName,
        string LedgerAccount,
        decimal Amount);

    private sealed record LedgerPostingReceipt(string PostingId, decimal Amount);
}
