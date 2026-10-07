using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CarParkOccupancy.Api.Tests;

[CollectionDefinition("ApiHost", DisableParallelization = true)]
public sealed class ApiHostCollection;

[Collection("ApiHost")]
public sealed class HttpOccupancyPipelineTests
{
    private const string Secret = "super-secret-value";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Prediction_and_dashboard_use_http_snapshots()
    {
        var handler = new RecordingHandler(_ => JsonResponse(SnapshotJson()));
        await using var factory = new HttpApiFactory("https://occupancy.example/v1", handler);
        using var client = factory.CreateClient();

        var apiResponse = await client.GetAsync("/api/carparks/CP001/predictions");
        apiResponse.EnsureSuccessStatusCode();
        var prediction = await apiResponse.Content.ReadFromJsonAsync<CarParkPredictionResult>(JsonOptions);
        Assert.NotNull(prediction);
        Assert.Equal("CP001", prediction.CarParkCode);
        Assert.Equal([1, 2, 3, 4, 5, 6], prediction.Predictions.Select(item => item.HorizonHours).ToArray());
        Assert.All(prediction.Predictions, item => Assert.Equal(37.0, item.PredictedOccupancyPercent));

        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"prediction-grid\"", html);
        Assert.Contains("CP001", html);
        Assert.Contains("CP002", html);
        Assert.Contains("37.0%", html);
        Assert.Contains("82.0%", html);
        Assert.DoesNotContain("data-testid=\"data-unavailable\"", html);
        Assert.DoesNotContain("data-testid=\"sample-banner\"", html);
        Assert.DoesNotContain(Secret, html, StringComparison.Ordinal);
        Assert.Contains(Secret, handler.AuthHeaders);
        Assert.All(handler.RequestUris, uri => Assert.DoesNotContain(Secret, uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_http_base_url_returns_503_without_the_auth_header_value()
    {
        await using var factory = new HttpApiFactory(baseUrl: "", new RecordingHandler(_ => JsonResponse("[]")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/carparks");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("BaseUrl", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);

        var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, home.StatusCode);
        Assert.Contains("data-testid=\"data-unavailable\"", html);
        Assert.Contains("CarParkData:Http:BaseUrl", html);
        Assert.DoesNotContain(Secret, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upstream_500_is_bad_gateway()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("nope", Encoding.UTF8, "text/plain")
        });
        await using var factory = new HttpApiFactory("https://occupancy.example/v1", handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/carparks/CP001/predictions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("HTTP 500", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
    }

    private static string SnapshotJson()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-8);
        var rows = new List<string>();
        for (var hour = 0; hour < 8; hour++)
        {
            var time = start.AddHours(hour).ToString("O");
            rows.Add(Row("CP001", time, 37));
            rows.Add(Row("CP002", time, 82));
        }

        return "[" + string.Join(',', rows) + "]";
    }

    private static string Row(string code, string time, int occupied) =>
        $$"""
        {"CarParkCode":"{{code}}","SnapshotTime":"{{time}}","CountingCategory":"PrivateCar","Capacity":100,"Occupied":{{occupied}}}
        """;

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string> RequestUris { get; } = [];

        public List<string?> AuthHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            AuthHeaders.Add(request.Headers.TryGetValues("X-Api-Key", out var values)
                ? string.Join(',', values)
                : null);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class HttpApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _baseUrl;
        private readonly RecordingHandler _handler;

        public HttpApiFactory(string baseUrl, RecordingHandler handler)
        {
            _baseUrl = baseUrl;
            _handler = handler;
            Environment.SetEnvironmentVariable("CarParkData__Source", "Http");
            Environment.SetEnvironmentVariable("CarParkData__UseSampleSnapshots", "false");
            Environment.SetEnvironmentVariable("ConnectionStrings__CarParkDb", "");
            Environment.SetEnvironmentVariable("CarParkData__Http__BaseUrl", baseUrl);
            Environment.SetEnvironmentVariable("CarParkData__Http__AuthHeaderName", "X-Api-Key");
            Environment.SetEnvironmentVariable("CarParkData__Http__AuthHeaderValue", Secret);
            Environment.SetEnvironmentVariable("CarParkData__Http__TimeoutSeconds", "5");
            Environment.SetEnvironmentVariable("Ui__ApiTimeoutSeconds", "15");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("CarParkData:Source", "Http");
            builder.UseSetting("CarParkData:UseSampleSnapshots", "false");
            builder.UseSetting("ConnectionStrings:CarParkDb", "");
            builder.UseSetting("CarParkData:Http:BaseUrl", _baseUrl);
            builder.UseSetting("CarParkData:Http:AuthHeaderName", "X-Api-Key");
            builder.UseSetting("CarParkData:Http:AuthHeaderValue", Secret);
            builder.UseSetting("CarParkData:Http:TimeoutSeconds", "5");
            builder.UseSetting("Ui:ApiTimeoutSeconds", "15");
        }

        public override async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("CarParkData__Source", null);
            Environment.SetEnvironmentVariable("CarParkData__UseSampleSnapshots", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__CarParkDb", null);
            Environment.SetEnvironmentVariable("CarParkData__Http__BaseUrl", null);
            Environment.SetEnvironmentVariable("CarParkData__Http__AuthHeaderName", null);
            Environment.SetEnvironmentVariable("CarParkData__Http__AuthHeaderValue", null);
            Environment.SetEnvironmentVariable("CarParkData__Http__TimeoutSeconds", null);
            await base.DisposeAsync();
        }

        protected override void ConfigureClient(HttpClient client)
        {
            Services.GetRequiredService<OccupancyHttpHandlerOverride>().HandlerFactory = () => _handler;
            Services.GetRequiredService<CarParkApiHandlerOverride>().HandlerFactory ??= () => Server.CreateHandler();
            base.ConfigureClient(client);
        }
    }
}
