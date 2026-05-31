using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Services;
using System.ComponentModel;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.McpTools;

/// <summary>
/// Weather MCP tool for testing inner agent integration.
/// Provides weather data for various locations to validate tool invocation flow.
/// </summary>
[McpServerToolType]
public sealed class WeatherTools
{
    private readonly WeatherService weatherService;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WeatherTools(WeatherService weatherService)
    {
        this.weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
    }

    /// <summary>
    /// Get current weather for a specified location.
    /// This tool is designed to test the inner agent weather test flow.
    /// </summary>
    /// <param name="location">The city or location to get weather for (e.g., "Ho Chi Minh City", "New York", "Tokyo")</param>
    /// <returns>JSON-serialized weather data including temperature, condition, humidity, and wind speed</returns>
    [McpServerTool, Description("Get current weather for a location. Returns temperature (°C), condition, humidity (%), and wind speed (km/h).")]
    public async Task<string> GetWeather([Description("Location name (city or country, e.g., 'Ho Chi Minh City', 'New York', 'Tokyo')")] string location)
    {
        try
        {
            var weather = await weatherService.GetWeatherAsync(location);
            return JsonSerializer.Serialize(new
            {
                success = true,
                data = new
                {
                    weather.Location,
                    weather.Temperature,
                    weather.Condition,
                    weather.Humidity,
                    weather.WindSpeed
                },
                message = $"Weather for {weather.Location}: {weather.Condition}, {weather.Temperature}°C"
            }, JsonOptions);
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "InvalidArgumentError",
                message = ex.Message
            }, JsonOptions);
        }
        catch (KeyNotFoundException ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "LocationNotFoundError",
                message = ex.Message
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.GetType().Name,
                message = ex.Message
            }, JsonOptions);
        }
    }
}
