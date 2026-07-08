namespace IncidentFactory.Api.Telemetry;

public sealed class IncidentFactoryOpenTelemetryOptions
{
    public const string ExporterNone = "none";
    public const string ExporterConsole = "console";
    public const string ExporterOtlp = "otlp";

    public string Exporter { get; init; } = ExporterNone;
    public Uri? OtlpEndpoint { get; init; }

    public bool IsEnabled => Exporter != ExporterNone;
    public bool UsesConsoleExporter => Exporter == ExporterConsole;
    public bool UsesOtlpExporter => Exporter == ExporterOtlp;

    public static IncidentFactoryOpenTelemetryOptions FromEnvironment()
    {
        var explicitExporter = ReadEnvironment("IF_OTEL_EXPORTER");
        var endpointValue = ReadEnvironment("IF_OTEL_OTLP_ENDPOINT")
            ?? ReadEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT");
        var endpoint = TryReadEndpoint(endpointValue);
        var exporter = NormalizeExporter(explicitExporter, endpoint);

        return new IncidentFactoryOpenTelemetryOptions
        {
            Exporter = exporter,
            OtlpEndpoint = endpoint
        };
    }

    private static string NormalizeExporter(string? explicitExporter, Uri? endpoint)
    {
        if (string.IsNullOrWhiteSpace(explicitExporter))
        {
            return endpoint is null ? ExporterNone : ExporterOtlp;
        }

        return explicitExporter.Trim().ToLowerInvariant() switch
        {
            ExporterConsole => ExporterConsole,
            ExporterOtlp => ExporterOtlp,
            "off" or "disabled" or ExporterNone => ExporterNone,
            _ => ExporterNone
        };
    }

    private static Uri? TryReadEndpoint(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

    private static string? ReadEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
