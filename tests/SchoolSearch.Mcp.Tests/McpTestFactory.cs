using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Hosts the real MCP app in-process. The named "catalog" HttpClient gets a stub primary handler
/// that serves canned Api JSON, so no network and no Api process are involved.
/// Each test creates its own factory, so the MCP endpoint's per-IP rate limit starts empty.
/// </summary>
public sealed class McpTestFactory : WebApplicationFactory<Program>
{
    public CatalogStubHandler Catalog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("catalog").ConfigurePrimaryHttpMessageHandler(() => Catalog);
        });
    }
}

/// <summary>
/// Serves the Api's public contract (SPEC-C.md) from canned JSON. Every request URL is recorded.
/// Set <see cref="UpstreamFailure"/> to answer every call with a 500 that carries a stack-like body,
/// so a leak into the tool result is detectable.
/// </summary>
public sealed class CatalogStubHandler : HttpMessageHandler
{
    public bool UpstreamFailure { get; set; }

    public List<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("Request without a URI.");
        Requests.Add(url);

        if (UpstreamFailure)
        {
            return Task.FromResult(Plain(HttpStatusCode.InternalServerError, UpstreamStackTrace));
        }

        var response = url.AbsolutePath switch
        {
            "/api/v1/schools" => Json(HttpStatusCode.OK, SchoolsPageJson),
            "/api/v1/catchment" => Json(HttpStatusCode.OK, CatchmentNoDataJson),
            _ => Json(HttpStatusCode.NotFound, NotFoundJson),
        };
        return Task.FromResult(response);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Plain(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private const string UpstreamStackTrace = """
        Unhandled exception processing request.
           at SchoolSearch.Api.Search.SearchEndpoints.<<MapSearchEndpoints>b__0>d.MoveNext() in /src/src/SchoolSearch.Api/Search/SearchEndpoints.cs:line 42
           at SchoolSearch.Api.Program.Main(String[] args) in /src/src/SchoolSearch.Api/Program.cs:line 9
        """;

    // One published school (complete ScoreBlock) and one suppressed school (score null, cohort 7 < threshold).
    private const string SchoolsPageJson = """
        {
          "items": [
            {
              "slug": "szkola-podstawowa-1",
              "name": "Szkoła Podstawowa nr 1",
              "gminaTeryt": "1465011",
              "gminaName": "Warszawa",
              "isPublic": true,
              "isSpecial": false,
              "latitude": 52.2297,
              "longitude": 21.0122,
              "distanceKm": null,
              "scoreStatus": "published",
              "score": {
                "score": 61.4,
                "reason": null,
                "cohortSize": 54,
                "intervalLow": 57.9,
                "intervalHigh": 64.9,
                "methodologyVersion": "0.1-draft",
                "examYear": 2025,
                "asOf": "2026-06-30",
                "source": "OKE 2025"
              }
            },
            {
              "slug": "szkola-podstawowa-2",
              "name": "Szkoła Podstawowa nr 2",
              "gminaTeryt": "1465011",
              "gminaName": "Warszawa",
              "isPublic": false,
              "isSpecial": false,
              "latitude": null,
              "longitude": null,
              "distanceKm": null,
              "scoreStatus": "insufficient_cohort",
              "score": {
                "score": null,
                "reason": "insufficient_cohort",
                "cohortSize": 7,
                "intervalLow": null,
                "intervalHigh": null,
                "methodologyVersion": "0.1-draft",
                "examYear": 2025,
                "asOf": "2026-06-30",
                "source": "OKE 2025"
              }
            }
          ],
          "page": 1,
          "pageSize": 20,
          "total": 2
        }
        """;

    // Gmina without a resolution: the contract says no_data, never a synthesised boundary.
    private const string CatchmentNoDataJson = """
        {
          "gminaTeryt": "1465011",
          "coverage": { "state": "none", "resolutionUrl": null, "resolutionDate": null },
          "match": "no_data",
          "schools": [],
          "disclaimer": "Dla tej gminy nie ma danych o obwodach szkół. Obwód ustala gmina; sprawdź uchwałę gminy."
        }
        """;

    private const string NotFoundJson = """
        {"error":{"code":"not_found","message":"Nie znaleziono.","traceId":"stub"}}
        """;
}
