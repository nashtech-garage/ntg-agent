using NTG.Agent.MCP.Server.McpTools;
using NTG.Agent.MCP.Server.Services;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.Tests.Services;

public sealed class WeatherToolsTests
{
    private readonly WeatherService weatherService;
    private readonly WeatherTools weatherTools;

    public WeatherToolsTests()
    {
        weatherService = new WeatherService();
        weatherTools = new WeatherTools(weatherService);
    }

    [Test]
    public async Task GetWeather_WithValidLocation_ReturnsSuccessfulWeatherData()
    {
        // Arrange
        var location = "Ho Chi Minh City";

        // Act
        var result = await weatherTools.GetWeather(location);
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.True);
        
        var hasData = jsonData.TryGetProperty("data", out var dataElement);
        Assert.That(hasData, Is.True);
        Assert.That(dataElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(jsonData.TryGetProperty("message", out _), Is.True);

        var data = dataElement;
        Assert.That(data.GetProperty("location").GetString(), Is.EqualTo("Ho Chi Minh City"));
        Assert.That(data.GetProperty("temperature").GetInt32(), Is.EqualTo(28));
        Assert.That(data.GetProperty("condition").GetString(), Is.EqualTo("Partly Cloudy"));
        Assert.That(data.GetProperty("humidity").GetInt32(), Is.EqualTo(75));
        Assert.That(data.GetProperty("windSpeed").GetInt32(), Is.EqualTo(12));
    }

    [Test]
    public async Task GetWeather_WithInvalidLocation_ReturnsErrorResponse()
    {
        // Arrange
        var location = "NonExistentLocation123";

        // Act
        var result = await weatherTools.GetWeather(location);
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("LocationNotFoundError"));
        Assert.That(jsonData.GetProperty("message").GetString(), Does.Contain("not found"));
    }

    [Test]
    public async Task GetWeather_WithEmptyLocation_ReturnsArgumentError()
    {
        // Arrange
        var location = "";

        // Act
        var result = await weatherTools.GetWeather(location);
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonData.GetProperty("error").GetString(), Is.EqualTo("InvalidArgumentError"));
        Assert.That(jsonData.GetProperty("message").GetString(), Does.Contain("empty"));
    }

    [TestCase("New York")]
    [TestCase("London")]
    [TestCase("Tokyo")]
    [TestCase("Dubai")]
    public async Task GetWeather_WithMultipleLocations_ReturnsCorrectData(string location)
    {
        // Act
        var result = await weatherTools.GetWeather(location);
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(jsonData.GetProperty("success").GetBoolean(), Is.True);
        var data = jsonData.GetProperty("data");
        Assert.That(data.GetProperty("location").GetString(), Is.EqualTo(location));
    }

    [Test]
    public async Task GetWeather_ResponseFormat_IncludesRequiredFields()
    {
        // Act
        var result = await weatherTools.GetWeather("Bangkok");
        var jsonData = JsonSerializer.Deserialize<JsonElement>(result);

        // Assert
        Assert.That(jsonData.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(jsonData.TryGetProperty("success", out _), Is.True);
        Assert.That(jsonData.TryGetProperty("data", out _), Is.True);
        Assert.That(jsonData.TryGetProperty("message", out _), Is.True);

        var data = jsonData.GetProperty("data");
        Assert.That(data.TryGetProperty("location", out _), Is.True);
        Assert.That(data.TryGetProperty("temperature", out _), Is.True);
        Assert.That(data.TryGetProperty("condition", out _), Is.True);
        Assert.That(data.TryGetProperty("humidity", out _), Is.True);
        Assert.That(data.TryGetProperty("windSpeed", out _), Is.True);
    }
}
