using IncidentFactory.Api.IncidentCompass;
using IncidentFactory.Api.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFactory.Tests;

public sealed class OpenTelemetrySetupTests
{
    [Fact]
    public void OpenTelemetryDefaultsToDisabledWithoutCollectorConfiguration()
    {
        using var environment = new EnvironmentVariableScope(
            "IF_OTEL_EXPORTER",
            "IF_OTEL_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_ENDPOINT");

        var options = IncidentFactoryOpenTelemetryOptions.FromEnvironment();

        Assert.False(options.IsEnabled);
        Assert.Equal(IncidentFactoryOpenTelemetryOptions.ExporterNone, options.Exporter);
        Assert.Null(options.OtlpEndpoint);
    }

    [Fact]
    public void OpenTelemetryCanBeRegisteredWithoutExternalInfrastructure()
    {
        using var environment = new EnvironmentVariableScope(
            "IF_OTEL_EXPORTER",
            "IF_OTEL_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_ENDPOINT");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        var telemetryOptions = IncidentFactoryOpenTelemetryOptions.FromEnvironment();
        var incidentCompassOptions = new IncidentCompassOptions();

        builder.AddIncidentFactoryOpenTelemetry(incidentCompassOptions, telemetryOptions);

        using var app = builder.Build();
        var registered = app.Services.GetRequiredService<IncidentFactoryOpenTelemetryOptions>();
        Assert.Same(telemetryOptions, registered);
        Assert.False(registered.IsEnabled);
    }

    [Fact]
    public void OpenTelemetryOtlpRequiresExplicitExporterOrEndpointEnvironment()
    {
        using var environment = new EnvironmentVariableScope(
            "IF_OTEL_EXPORTER",
            "IF_OTEL_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_ENDPOINT");
        environment.Set("IF_OTEL_EXPORTER", "otlp");
        environment.Set("IF_OTEL_OTLP_ENDPOINT", "http://localhost:4317");

        var options = IncidentFactoryOpenTelemetryOptions.FromEnvironment();

        Assert.True(options.IsEnabled);
        Assert.True(options.UsesOtlpExporter);
        Assert.Equal(new Uri("http://localhost:4317"), options.OtlpEndpoint);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentVariableScope(params string[] names)
        {
            _originalValues = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);

            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public void Set(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
