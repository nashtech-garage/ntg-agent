using NTG.Agent.MCP.Server.McpTools;
using NTG.Agent.MCP.Server.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.Tests.Services;

public sealed class WeatherToolsTests
{
    private const string TestApiKey = "test-api-key";

    // Canned WeatherAPI.com current.json response for Hanoi.
    private const string HanoiResponse = """
        {
          "location": { "name": "Hanoi", "region": "", "country": "Vietnam", "lat": 21.03, "lon": 105.85, "localtime": "2026-06-22 14:26" },
          "current": {
            "temp_c": 31.6,
            "is_day": 1,
            "condition": { "text": "Partly cloudy", "icon": "//cdn.weatherapi.com/weather/64x64/day/116.png", "code": 1003 },
            "wind_kph": 13.0,
            "humidity": 70,
            "feelslike_c": 38.2,
            "uv": 7.5
          }
        }
        """;

    // Builds a WeatherTools whose HttpClient returns a fixed response, so tests never hit the network.
    private static WeatherTools CreateTools(HttpStatusCode status = HttpStatusCode.OK, string body = HanoiResponse, string? apiKey = TestApiKey)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var httpClient = new HttpClient(handler);
        var weatherService = new WeatherService(httpClient, apiKey);
        return new WeatherTools(weatherService);
    }

    [Test]
    public async Task GetWeather_WithValidLocation_ReturnsMappedWeatherData()
    {
        var tools = CreateTools();

        var result = await tools.GetWeather("Hanoi");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.True);

        var data = jsonData.GetProperty("data");
        Assert.That(data.GetProperty("city").GetString(), Is.EqualTo("Hanoi"));
        Assert.That(data.GetProperty("country").GetString(), Is.EqualTo("Vietnam"));
        Assert.That(data.GetProperty("temperatureC").GetDouble(), Is.EqualTo(31.6));
        Assert.That(data.GetProperty("condition").GetString(), Is.EqualTo("Partly cloudy"));
        Assert.That(data.GetProperty("humidity").GetInt32(), Is.EqualTo(70));
        Assert.That(data.GetProperty("windKph").GetDouble(), Is.EqualTo(13.0));
        Assert.That(data.GetProperty("feelsLikeC").GetDouble(), Is.EqualTo(38.2));
        Assert.That(data.GetProperty("localTime").GetString(), Is.EqualTo("2026-06-22 14:26"));
        Assert.That(data.GetProperty("iconUrl").GetString(), Is.EqualTo("https://cdn.weatherapi.com/weather/64x64/day/116.png"));
        Assert.That(data.GetProperty("uv").GetDouble(), Is.EqualTo(7.5));
        Assert.That(data.GetProperty("isDay").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetWeather_WithEmptyLocation_ReturnsArgumentError()
    {
        var tools = CreateTools();

        var result = await tools.GetWeather("");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("InvalidArgumentError"));
        Assert.That(jsonData.GetProperty("message").GetString(), Does.Contain("empty"));
    }

    [Test]
    public async Task GetWeather_WhenLocationNotFound_ReturnsLocationNotFoundError()
    {
        // WeatherAPI returns HTTP 400 with error code 1006 for an unknown location.
        var tools = CreateTools(HttpStatusCode.BadRequest,
            """{ "error": { "code": 1006, "message": "No matching location found." } }""");

        var result = await tools.GetWeather("NonExistentLocation123");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("LocationNotFoundError"));
        Assert.That(jsonData.GetProperty("message").GetString(), Does.Contain("not found"));
    }

    [Test]
    public async Task GetWeather_WhenApiKeyMissing_ReturnsWeatherServiceError()
    {
        var tools = CreateTools(apiKey: "");

        var result = await tools.GetWeather("Hanoi");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("WeatherServiceError"));
        Assert.That(jsonData.GetProperty("message").GetString(), Does.Contain("key"));
    }

    [Test]
    public async Task GetWeather_WhenApiKeyRejected_ReturnsWeatherServiceError()
    {
        // WeatherAPI returns HTTP 401 with error code 2006 for an invalid key.
        var tools = CreateTools(HttpStatusCode.Unauthorized,
            """{ "error": { "code": 2006, "message": "API key provided is invalid" } }""");

        var result = await tools.GetWeather("Hanoi");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("WeatherServiceError"));
    }

    [Test]
    public async Task GetWeather_ResponseFormat_IncludesRequiredFields()
    {
        var tools = CreateTools();

        var result = await tools.GetWeather("Hanoi");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.That(jsonData.TryGetProperty("success", out _), Is.True);
        Assert.That(jsonData.TryGetProperty("data", out _), Is.True);
        Assert.That(jsonData.TryGetProperty("message", out _), Is.True);

        var data = jsonData.GetProperty("data");
        Assert.That(data.TryGetProperty("city", out _), Is.True);
        Assert.That(data.TryGetProperty("country", out _), Is.True);
        Assert.That(data.TryGetProperty("localTime", out _), Is.True);
        Assert.That(data.TryGetProperty("temperatureC", out _), Is.True);
        Assert.That(data.TryGetProperty("condition", out _), Is.True);
        Assert.That(data.TryGetProperty("iconUrl", out _), Is.True);
        Assert.That(data.TryGetProperty("humidity", out _), Is.True);
        Assert.That(data.TryGetProperty("windKph", out _), Is.True);
        Assert.That(data.TryGetProperty("feelsLikeC", out _), Is.True);
        Assert.That(data.TryGetProperty("uv", out _), Is.True);
        Assert.That(data.TryGetProperty("isDay", out _), Is.True);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHttpMessageHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
