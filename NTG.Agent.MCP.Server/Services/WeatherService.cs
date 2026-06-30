using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NTG.Agent.MCP.Server.Services;

/// <summary>
/// Mock weather service for testing inner agent weather tool integration.
/// Returns deterministic weather data for common locations.
/// </summary>
public sealed class WeatherService
{
    private static readonly Dictionary<string, WeatherData> MockWeatherDatabase = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Ho Chi Minh City", new WeatherData { Location = "Ho Chi Minh City", Temperature = 28, Condition = "Partly Cloudy", Humidity = 75, WindSpeed = 12 } },
        { "Hanoi", new WeatherData { Location = "Hanoi", Temperature = 25, Condition = "Rainy", Humidity = 85, WindSpeed = 18 } },
        { "Da Nang", new WeatherData { Location = "Da Nang", Temperature = 26, Condition = "Sunny", Humidity = 70, WindSpeed = 15 } },
        { "New York", new WeatherData { Location = "New York", Temperature = 15, Condition = "Cloudy", Humidity = 65, WindSpeed = 20 } },
        { "London", new WeatherData { Location = "London", Temperature = 12, Condition = "Rainy", Humidity = 80, WindSpeed = 25 } },
        { "Tokyo", new WeatherData { Location = "Tokyo", Temperature = 18, Condition = "Clear", Humidity = 55, WindSpeed = 10 } },
        { "Sydney", new WeatherData { Location = "Sydney", Temperature = 22, Condition = "Sunny", Humidity = 60, WindSpeed = 14 } },
        { "Dubai", new WeatherData { Location = "Dubai", Temperature = 35, Condition = "Clear", Humidity = 40, WindSpeed = 8 } },
        { "Singapore", new WeatherData { Location = "Singapore", Temperature = 29, Condition = "Partly Cloudy", Humidity = 80, WindSpeed = 11 } },
        { "Bangkok", new WeatherData { Location = "Bangkok", Temperature = 30, Condition = "Thunderstorm", Humidity = 85, WindSpeed = 22 } },
    };

    public async Task<WeatherData> GetWeatherAsync(string location)
    {
        // Simulate API call delay
        await Task.Delay(100);

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("Location cannot be empty.", nameof(location));
        }

        var found = MockWeatherDatabase.TryGetValue(location, out var weather);
        if (!found)
        {
            throw new KeyNotFoundException($"Weather data for location '{location}' not found. Available locations: {string.Join(", ", MockWeatherDatabase.Keys)}");
        }

        return weather!;
    }
}

public sealed class WeatherData
{
    public string Location { get; set; } = string.Empty;
    public int Temperature { get; set; }
    public string Condition { get; set; } = string.Empty;
    public int Humidity { get; set; }
    public int WindSpeed { get; set; }

    public override string ToString()
    {
        return $"{Location}: {Temperature}°C, {Condition}, Humidity: {Humidity}%, Wind: {WindSpeed}km/h";
    }
}
