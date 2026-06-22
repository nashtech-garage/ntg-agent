using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Services;
using System.ComponentModel;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.McpTools;

/// <summary>
/// Weather MCP tool. Fetches the real current weather for a location from OpenWeather
/// and returns it in a shape that maps directly to the browser show_weather card.
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
    /// Get current weather for a specified location from WeatherAPI.com.
    /// </summary>
    /// <param name="location">The city or location to get weather for (e.g., "Ho Chi Minh City", "New York", "Tokyo")</param>
    /// <returns>JSON-serialized weather data (city, country, temperatureC, condition, humidity, windKph, feelsLikeC)</returns>
    [McpServerTool(Name = "get_weather"), Description("Get the real current weather for a city or location from " +
        "WeatherAPI.com. Call this whenever the user asks about the weather, temperature, or conditions. The UI " +
        "renders the result as a weather card automatically, so you do not need any other tool to show it. After " +
        "calling this, write a short, friendly description of the current weather for the user (temperature and how " +
        "it feels, the condition, and anything notable about humidity, wind or UV). Never invent weather values; if " +
        "the call returns success=false, tell the user what went wrong in plain text.")]
    public async Task<string> GetWeather([Description("Location name, optionally with a country code, e.g. 'Hanoi', 'London,GB' or 'Tokyo'.")] string location)
    {
        try
        {
            var weather = await weatherService.GetWeatherAsync(location);
            return JsonSerializer.Serialize(new
            {
                success = true,
                data = new
                {
                    city = weather.Location,
                    country = weather.Country,
                    localTime = weather.LocalTime,
                    temperatureC = weather.Temperature,
                    condition = weather.Condition,
                    iconUrl = weather.IconUrl,
                    humidity = weather.Humidity,
                    windKph = weather.WindSpeed,
                    feelsLikeC = weather.FeelsLike,
                    uv = weather.Uv,
                    isDay = weather.IsDay
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
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "WeatherServiceError",
                message = ex.Message
            }, JsonOptions);
        }
    }
}
