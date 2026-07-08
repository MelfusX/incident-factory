namespace IncidentFactory.Api.IncidentCompass;

public sealed record IncidentEnvelopeRequest
{
    public string SourceKind { get; init; } = IncidentCompassOptions.SourceKind;
    public string? ServiceName { get; init; }
    public string? Environment { get; init; }
    public string? Severity { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public IncidentCorrelation? Correlation { get; init; }
    public IReadOnlyDictionary<string, object?>? Attributes { get; init; }
    public IReadOnlyDictionary<string, object?>? Payload { get; init; }
}

public sealed record IncidentCorrelation(string? TraceId, string? SpanId, string? ExternalId);

public sealed record IncidentDeliveryResult(
    bool Attempted,
    bool Success,
    string Url,
    int? StatusCode,
    string? SignalId,
    string? FaultId,
    string? JobId,
    string? Error,
    string? ResponsePreview,
    DateTimeOffset DeliveredAtUtc);
