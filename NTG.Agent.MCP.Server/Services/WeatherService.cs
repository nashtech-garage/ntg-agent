using System.Net;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.Services;

/// <summary>
/// Fetches the current weather for a location from WeatherAPI.com's "current" endpoint.
/// WeatherAPI accepts a city name directly (no geocoding step) and already returns metric
/// values (°C and km/h). The API key is read from configuration (WeatherApi:ApiKey).
/// </summary>
public sealed class WeatherService
{
    private const string CurrentWeatherEndpoint = "https://api.weatherapi.com/v1/current.json";
    private const string MissingKeyPlaceholder = "YOUR_WEATHERAPI_KEY_HERE";

    // WeatherAPI error codes (returned in the error.code field).
    private const int LocationNotFoundCode = 1006;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public WeatherService(HttpClient httpClient, string? apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey;
    }

    /// <summary>
    /// Gets the current weather for <paramref name="location"/> from WeatherAPI.com.
    /// </summary>
    /// <exception cref="ArgumentException">The location is empty.</exception>
    /// <exception cref="KeyNotFoundException">No matching location was found.</exception>
    /// <exception cref="InvalidOperationException">The API key is missing/invalid or the service failed.</exception>
    public async Task<WeatherData> GetWeatherAsync(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("Location cannot be empty.", nameof(location));
        }

        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == MissingKeyPlaceholder)
        {
            throw new InvalidOperationException("The WeatherAPI key is not configured on the server (WeatherApi:ApiKey).");
        }

        var requestUri = $"{CurrentWeatherEndpoint}?key={_apiKey}&q={Uri.EscapeDataString(location)}&aqi=no";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUri);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to reach the weather service: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ThrowForError(response.StatusCode, json, location);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var locationEl = root.GetProperty("location");
            var resolvedName = locationEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? location : location;
            var country = locationEl.TryGetProperty("country", out var countryEl) ? countryEl.GetString() : null;
            var localTime = locationEl.TryGetProperty("localtime", out var localTimeEl) ? localTimeEl.GetString() : null;

            var current = root.GetProperty("current");
            double temperature = current.TryGetProperty("temp_c", out var tempEl) ? Math.Round(tempEl.GetDouble(), 1) : 0;
            double feelsLike = current.TryGetProperty("feelslike_c", out var feelsEl) ? Math.Round(feelsEl.GetDouble(), 1) : 0;
            int humidity = current.TryGetProperty("humidity", out var humEl) ? humEl.GetInt32() : 0;
            double windKph = current.TryGetProperty("wind_kph", out var windEl) ? Math.Round(windEl.GetDouble(), 1) : 0;
            double? uv = current.TryGetProperty("uv", out var uvEl) ? uvEl.GetDouble() : null;
            // WeatherAPI reports is_day as 1 (day) or 0 (night).
            bool? isDay = current.TryGetProperty("is_day", out var isDayEl) ? isDayEl.GetInt32() == 1 : null;

            string condition = string.Empty;
            string? iconUrl = null;
            if (current.TryGetProperty("condition", out var condObj))
            {
                if (condObj.TryGetProperty("text", out var condTextEl))
                    condition = condTextEl.GetString()?.Trim() ?? string.Empty;
                if (condObj.TryGetProperty("icon", out var iconEl))
                    iconUrl = NormalizeIconUrl(iconEl.GetString());
            }

            return new WeatherData
            {
                Location = resolvedName,
                Country = country,
                LocalTime = localTime,
                Temperature = temperature,
                Condition = condition,
                IconUrl = iconUrl,
                Humidity = humidity,
                WindSpeed = windKph,
                FeelsLike = feelsLike,
                Uv = uv,
                IsDay = isDay,
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse the weather response: {ex.Message}", ex);
        }
        catch (KeyNotFoundException ex)
        {
            // GetProperty throws KeyNotFoundException for a missing required field; treat as a malformed response.
            throw new InvalidOperationException($"Unexpected weather response shape: {ex.Message}", ex);
        }
    }

    // WeatherAPI returns a protocol-relative icon URL (e.g. "//cdn.weatherapi.com/...png").
    // Promote it to an absolute https URL so the browser can load it directly.
    private static string? NormalizeIconUrl(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        if (icon.StartsWith("//")) return $"https:{icon}";
        return icon;
    }

    // WeatherAPI returns errors as { "error": { "code": <int>, "message": "..." } } with a 4xx status.
    private static void ThrowForError(HttpStatusCode status, string json, string location)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                if (errorEl.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
                    code = codeEl.GetInt32();
                if (errorEl.TryGetProperty("message", out var msgEl))
                    message = msgEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to a generic message.
        }

        if (code == LocationNotFoundCode)
        {
            throw new KeyNotFoundException($"Weather data for location '{location}' was not found.");
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException($"The WeatherAPI key was rejected ({(int)status}). Check WeatherApi:ApiKey. {message}".Trim());
        }

        throw new InvalidOperationException($"Weather service returned status {(int)status}. {message}".Trim());
    }
}

public sealed class WeatherData
{
    public string Location { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? LocalTime { get; set; }
    public double Temperature { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int Humidity { get; set; }
    public double WindSpeed { get; set; }
    public double FeelsLike { get; set; }
    public double? Uv { get; set; }
    public bool? IsDay { get; set; }

    public override string ToString()
    {
        return $"{Location}: {Temperature}°C, {Condition}, Humidity: {Humidity}%, Wind: {WindSpeed}km/h";
    }
}
