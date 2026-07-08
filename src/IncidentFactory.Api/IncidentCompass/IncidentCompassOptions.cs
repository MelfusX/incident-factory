namespace IncidentFactory.Api.IncidentCompass;

public sealed class IncidentCompassOptions
{
    public const string SourceKind = "otel";

    public string BaseUrl { get; init; } = "http://localhost:5198";
    public string ServiceName { get; init; } = "incident-factory";
    public string Environment { get; init; } = "demo";
    public string DemoUserId { get; init; } = "incident-factory";
    public string DemoTenantId { get; init; } = "demo-tenant";
    public string IncidentsPath { get; init; } = "/api/v1/incidents";

    public string IncidentsUrl => new Uri(new Uri(BaseUrl, UriKind.Absolute), IncidentsPath).ToString();

    public static IncidentCompassOptions FromEnvironment()
    {
        return new IncidentCompassOptions
        {
            BaseUrl = ReadEnvironment("IC_BASE_URL", "http://localhost:5198").TrimEnd('/'),
            ServiceName = ReadEnvironment("IF_SERVICE_NAME", "incident-factory"),
            Environment = ReadEnvironment("IF_ENVIRONMENT", "demo"),
            DemoUserId = ReadEnvironment("IC_DEMO_USER_ID", "incident-factory"),
            DemoTenantId = ReadEnvironment("IC_DEMO_TENANT_ID", "demo-tenant")
        };
    }

    private static string ReadEnvironment(string name, string fallback)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
