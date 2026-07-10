using System.Net;
using System.Text;
using IncidentFactory.Api.IncidentCompass;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IncidentFactory.Tests;

public sealed class IncidentCompassReportClientTests
{
    [Fact]
    public async Task GetLatestReportForFaultReturnsNotReadyWhenNoReportIsPublished()
    {
        var faultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var client = CreateClient(request =>
        {
            Assert.Equal($"/api/v1/faults/{faultId:D}/ledger", request.RequestUri?.PathAndQuery);

            return JsonResponse($$"""
                {
                  "faultId": "{{faultId:D}}",
                  "events": [
                    {
                      "id": 1,
                      "jobId": "{{jobId:D}}",
                      "attempt": 1,
                      "eventType": "ToolResult",
                      "payloadRef": "artifact:test",
                      "configHash": "config-test",
                      "createdAtUtc": "2026-07-09T10:00:00Z"
                    }
                  ]
                }
                """);
        });

        var result = await client.GetLatestReportForFaultAsync(faultId.ToString("D"), CancellationToken.None);

        Assert.Equal(IncidentCompassReportLookupStatus.NotReady, result.Status);
        Assert.Equal(faultId.ToString("D"), result.FaultId);
        Assert.Null(result.ReportId);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task GetLatestReportForFaultFetchesNewestPublishedReport()
    {
        var faultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var oldReportId = Guid.NewGuid();
        var newReportId = Guid.NewGuid();
        var requestedPaths = new List<string>();
        var client = CreateClient(request =>
        {
            requestedPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);

            if (request.RequestUri?.AbsolutePath.EndsWith("/ledger", StringComparison.Ordinal) == true)
            {
                return JsonResponse($$"""
                    {
                      "faultId": "{{faultId:D}}",
                      "events": [
                        {
                          "id": 10,
                          "jobId": "{{jobId:D}}",
                          "attempt": 1,
                          "eventType": "ReportPublished",
                          "payloadRef": "report:{{oldReportId:D}}",
                          "configHash": "config-test",
                          "createdAtUtc": "2026-07-09T10:00:00Z"
                        },
                        {
                          "id": 11,
                          "jobId": "{{jobId:D}}",
                          "attempt": 1,
                          "eventType": "ReportPublished",
                          "payloadRef": "report:{{newReportId:D}}",
                          "configHash": "config-test",
                          "createdAtUtc": "2026-07-09T10:01:00Z"
                        }
                      ]
                    }
                    """);
            }

            Assert.Equal($"/api/v1/triage-reports/{newReportId:D}", request.RequestUri?.PathAndQuery);
            return JsonResponse($$"""
                {
                  "id": "{{newReportId:D}}",
                  "faultId": "{{faultId:D}}",
                  "status": "Completed",
                  "summary": "Checkout fault traced to a downstream timeout.",
                  "classification": "SingleFault",
                  "confidence": "High",
                  "isMassIssue": false,
                  "recommendedNextAction": "Inspect the checkout dependency timeout budget.",
                  "limitations": ["Synthetic demo evidence only."],
                  "configHash": "config-test",
                  "createdAtUtc": "2026-07-09T10:02:00Z",
                  "evidence": [
                    {
                      "id": "{{Guid.NewGuid():D}}",
                      "kind": "Artifact",
                      "artifactId": "{{Guid.NewGuid():D}}",
                      "reference": "artifact:trigger",
                      "quote": "checkout timed out",
                      "score": 0.91,
                      "artifactKind": "TriggerSignal",
                      "artifactDomainRef": "checkout.submit",
                      "artifactPayload": { "errorType": "TimeoutException" }
                    }
                  ]
                }
                """);
        });

        var result = await client.GetLatestReportForFaultAsync(faultId.ToString("D"), CancellationToken.None);

        Assert.Equal(IncidentCompassReportLookupStatus.Ready, result.Status);
        Assert.Equal(newReportId.ToString("D"), result.ReportId);
        Assert.Equal("Checkout fault traced to a downstream timeout.", result.Report?.Summary);
        Assert.Equal(
            [
                $"/api/v1/faults/{faultId:D}/ledger",
                $"/api/v1/triage-reports/{newReportId:D}"
            ],
            requestedPaths);
    }

    private static IncidentCompassReportClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("http://incidentcompass.test")
        };

        return new IncidentCompassReportClient(
            httpClient,
            new IncidentCompassOptions { BaseUrl = "http://incidentcompass.test" },
            NullLogger<IncidentCompassReportClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
