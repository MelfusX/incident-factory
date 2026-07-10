using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IncidentFactory.Api.IncidentCompass;

public interface IIncidentCompassReportClient
{
    Task<IncidentCompassReportLookupResponse> GetLatestReportForFaultAsync(
        string faultId,
        CancellationToken cancellationToken);
}

public sealed class IncidentCompassReportClient(
    HttpClient httpClient,
    IncidentCompassOptions options,
    ILogger<IncidentCompassReportClient> logger) : IIncidentCompassReportClient
{
    private const string ReportPublishedEventType = "ReportPublished";
    private const string ReportPayloadPrefix = "report:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IncidentCompassReportLookupResponse> GetLatestReportForFaultAsync(
        string faultId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(faultId, out var faultGuid))
        {
            throw new ArgumentException("faultId must be a GUID.", nameof(faultId));
        }

        var normalizedFaultId = faultGuid.ToString("D");
        var ledger = await GetAsync<FaultLedgerResponse>(
            options.GetFaultLedgerPath(normalizedFaultId),
            $"fault ledger for {normalizedFaultId}",
            cancellationToken);

        var reportEvent = ledger.Events
            .Where(static item => string.Equals(item.EventType, ReportPublishedEventType, StringComparison.Ordinal))
            .OrderByDescending(static item => item.Id)
            .FirstOrDefault();

        if (reportEvent is null)
        {
            return new IncidentCompassReportLookupResponse(
                IncidentCompassReportLookupStatus.NotReady,
                normalizedFaultId,
                null,
                "IncidentCompass has not published a triage report for this fault yet.",
                null);
        }

        if (!TryReadReportId(reportEvent.PayloadRef, out var reportId))
        {
            throw new IncidentCompassReportReadException(
                $"ReportPublished ledger event {reportEvent.Id} did not include a usable report payloadRef.");
        }

        var report = await GetAsync<IncidentCompassTriageReport>(
            options.GetTriageReportPath(reportId),
            $"triage report {reportId}",
            cancellationToken);

        return new IncidentCompassReportLookupResponse(
            IncidentCompassReportLookupStatus.Ready,
            normalizedFaultId,
            reportId,
            "IncidentCompass published a triage report for this fault.",
            report);
    }

    private async Task<T> GetAsync<T>(string path, string label, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Demo-User-Id", options.DemoUserId);
        request.Headers.TryAddWithoutValidation("X-Demo-Tenant-Id", options.DemoTenantId);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new IncidentCompassReportReadException(
                    $"IncidentCompass returned {(int)response.StatusCode} while reading {label}.",
                    response.StatusCode,
                    Preview(body));
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw new IncidentCompassReportReadException(
                $"IncidentCompass returned an empty {label} response.");
        }
        catch (IncidentCompassReportReadException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IncidentCompass report read failed while reading {Label}.", label);
            throw new IncidentCompassReportReadException(
                $"IncidentCompass report read failed while reading {label}: {ex.Message}",
                null,
                null,
                ex);
        }
    }

    private static bool TryReadReportId(string? payloadRef, out string reportId)
    {
        reportId = string.Empty;
        if (string.IsNullOrWhiteSpace(payloadRef) ||
            !payloadRef.StartsWith(ReportPayloadPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = payloadRef[ReportPayloadPrefix.Length..];
        if (!Guid.TryParse(value, out var parsed))
        {
            return false;
        }

        reportId = parsed.ToString("D");
        return true;
    }

    private static string? Preview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 1000 ? value : value[..1000];
    }

    private sealed record FaultLedgerResponse(
        string FaultId,
        IReadOnlyList<FaultLedgerEvent> Events);

    private sealed record FaultLedgerEvent(
        long Id,
        string JobId,
        int Attempt,
        string EventType,
        string? Role,
        string? ToolName,
        string? Decision,
        string? Rationale,
        string? DecisionReason,
        string? ToolStatus,
        int? TokensDelta,
        int? WorkersDelta,
        string? PayloadRef,
        string ConfigHash,
        DateTimeOffset CreatedAtUtc);
}

public sealed class IncidentCompassReportReadException : Exception
{
    public IncidentCompassReportReadException(
        string message,
        HttpStatusCode? upstreamStatusCode = null,
        string? responsePreview = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        ResponsePreview = responsePreview;
    }

    public HttpStatusCode? UpstreamStatusCode { get; }
    public string? ResponsePreview { get; }
}
