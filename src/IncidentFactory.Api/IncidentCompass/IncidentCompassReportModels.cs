using System.Text.Json;

namespace IncidentFactory.Api.IncidentCompass;

public enum IncidentCompassReportLookupStatus
{
    NoFault,
    NotReady,
    Ready
}

public sealed record IncidentCompassReportLookupResponse(
    IncidentCompassReportLookupStatus Status,
    string? FaultId,
    string? ReportId,
    string Message,
    IncidentCompassTriageReport? Report);

public sealed record IncidentCompassTriageReport(
    string Id,
    string FaultId,
    string Status,
    string Summary,
    string Classification,
    string Confidence,
    bool? IsMassIssue,
    string RecommendedNextAction,
    IReadOnlyList<string> Limitations,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<IncidentCompassReportEvidence> Evidence);

public sealed record IncidentCompassReportEvidence(
    string Id,
    string Kind,
    string ArtifactId,
    string Reference,
    string? Quote,
    double? Score,
    string ArtifactKind,
    string? ArtifactDomainRef,
    JsonElement ArtifactPayload);
