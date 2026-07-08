using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentFactory.Api.IncidentCompass;

public interface IIncidentCompassClient
{
    Task<IncidentDeliveryResult> SendAsync(IncidentEnvelopeRequest envelope, CancellationToken cancellationToken);
}

public sealed class IncidentCompassClient(
    HttpClient httpClient,
    IncidentCompassOptions options,
    ILogger<IncidentCompassClient> logger) : IIncidentCompassClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IncidentDeliveryResult> SendAsync(
        IncidentEnvelopeRequest envelope,
        CancellationToken cancellationToken)
    {
        var deliveredAtUtc = DateTimeOffset.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Post, options.IncidentsPath)
        {
            Content = JsonContent.Create(envelope, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("X-Demo-User-Id", options.DemoUserId);
        request.Headers.TryAddWithoutValidation("X-Demo-Tenant-Id", options.DemoTenantId);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var ids = TryReadResponseIds(responseBody);

            return new IncidentDeliveryResult(
                Attempted: true,
                Success: response.IsSuccessStatusCode,
                Url: options.IncidentsUrl,
                StatusCode: (int)response.StatusCode,
                SignalId: ids.SignalId,
                FaultId: ids.FaultId,
                JobId: ids.JobId,
                Error: response.IsSuccessStatusCode ? null : $"IncidentCompass returned {(int)response.StatusCode}.",
                ResponsePreview: Preview(responseBody),
                DeliveredAtUtc: deliveredAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IncidentCompass delivery failed.");
            return new IncidentDeliveryResult(
                Attempted: true,
                Success: false,
                Url: options.IncidentsUrl,
                StatusCode: null,
                SignalId: null,
                FaultId: null,
                JobId: null,
                Error: ex.Message,
                ResponsePreview: null,
                DeliveredAtUtc: deliveredAtUtc);
        }
    }

    private static (string? SignalId, string? FaultId, string? JobId) TryReadResponseIds(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return (null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            return (ReadValue(root, "signalId"), ReadValue(root, "faultId"), ReadValue(root, "jobId"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? ReadValue(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static string? Preview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 1000 ? value : value[..1000];
    }
}
