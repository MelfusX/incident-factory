using IncidentFactory.Api.IncidentCompass;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IncidentFactory.Api.Telemetry;

public static class IncidentFactoryOpenTelemetryExtensions
{
    public static WebApplicationBuilder AddIncidentFactoryOpenTelemetry(
        this WebApplicationBuilder builder,
        IncidentCompassOptions incidentCompassOptions,
        IncidentFactoryOpenTelemetryOptions telemetryOptions)
    {
        builder.Services.AddSingleton(telemetryOptions);

        if (!telemetryOptions.IsEnabled)
        {
            return builder;
        }

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, incidentCompassOptions))
            .WithTracing(tracing =>
            {
                tracing.AddSource(ScenarioTelemetry.ActivitySourceName);
                AddTracingExporter(tracing, telemetryOptions);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(ScenarioTelemetry.MeterName);
                AddMetricsExporter(metrics, telemetryOptions);
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            logging.SetResourceBuilder(CreateResourceBuilder(incidentCompassOptions));
            AddLoggingExporter(logging, telemetryOptions);
        });

        return builder;
    }

    private static void ConfigureResource(ResourceBuilder resource, IncidentCompassOptions options)
    {
        resource
            .AddService(serviceName: options.ServiceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment.name"] = options.Environment,
                ["deployment.environment"] = options.Environment
            });
    }

    private static ResourceBuilder CreateResourceBuilder(IncidentCompassOptions options)
    {
        return ResourceBuilder.CreateDefault()
            .AddService(serviceName: options.ServiceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment.name"] = options.Environment,
                ["deployment.environment"] = options.Environment
            });
    }

    private static void AddTracingExporter(
        TracerProviderBuilder tracing,
        IncidentFactoryOpenTelemetryOptions options)
    {
        if (options.UsesConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }
        else if (options.UsesOtlpExporter)
        {
            tracing.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, options));
        }
    }

    private static void AddMetricsExporter(
        MeterProviderBuilder metrics,
        IncidentFactoryOpenTelemetryOptions options)
    {
        if (options.UsesConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }
        else if (options.UsesOtlpExporter)
        {
            metrics.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, options));
        }
    }

    private static void AddLoggingExporter(
        OpenTelemetryLoggerOptions logging,
        IncidentFactoryOpenTelemetryOptions options)
    {
        if (options.UsesConsoleExporter)
        {
            logging.AddConsoleExporter();
        }
        else if (options.UsesOtlpExporter)
        {
            logging.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, options));
        }
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions exporter,
        IncidentFactoryOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is not null)
        {
            exporter.Endpoint = options.OtlpEndpoint;
        }
    }
}
