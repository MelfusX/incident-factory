using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentFactory.Api.HardScenarios;
using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Scenarios;
using IncidentFactory.Api.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var incidentCompassOptions = IncidentCompassOptions.FromEnvironment();
var openTelemetryOptions = IncidentFactoryOpenTelemetryOptions.FromEnvironment();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton(ScenarioCatalog.Create());
builder.Services.AddSingleton<ScenarioState>();
builder.Services.AddSingleton<ScenarioRunner>();
builder.Services.AddSingleton<HardScenarioRunner>();
builder.Services.AddHostedService<ScheduledScenarioWorker>();
builder.Services.AddSingleton(incidentCompassOptions);
builder.AddIncidentFactoryOpenTelemetry(incidentCompassOptions, openTelemetryOptions);
builder.Services.AddHttpClient<IIncidentCompassClient, IncidentCompassClient>((services, client) =>
{
    var options = services.GetRequiredService<IncidentCompassOptions>();
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "incident-factory",
    observedAtUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/runtime", (IncidentCompassOptions options) => Results.Ok(new
{
    serviceName = options.ServiceName,
    environment = options.Environment,
    incidentCompassBaseUrl = options.BaseUrl,
    sourceKind = IncidentCompassOptions.SourceKind,
    openTelemetry = new
    {
        exporter = openTelemetryOptions.Exporter,
        otlpEndpoint = openTelemetryOptions.OtlpEndpoint?.ToString()
    },
    hardScenarios = HardScenarioEndpointExtensions.GetHardScenarioViews()
}));

app.MapScenarioControlEndpoints();
app.MapScenarioBusinessEndpoints();
app.MapHardScenarioBusinessEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}
