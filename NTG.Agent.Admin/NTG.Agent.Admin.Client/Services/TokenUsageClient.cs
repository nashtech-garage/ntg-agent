using NTG.Agent.Common.Dtos.TokenUsage;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class TokenUsageClient(HttpClient httpClient)
{
    public async Task<TokenUsageStatsDto?> GetStatsAsync(DateTime? from = null, DateTime? to = null)
    {
        var url = BuildUrl("api/TokenUsage/stats", from, to);
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TokenUsageStatsDto>();
    }

    public async Task<List<UserTokenStatsDto>?> GetUserStatsAsync(DateTime? from = null, DateTime? to = null)
    {
        var url = BuildUrl("api/TokenUsage/by-user", from, to);
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<UserTokenStatsDto>>();
    }

    public async Task<PagedResult<TokenUsageDto>?> GetUsageHistoryAsync(
        int page = 1,
        int pageSize = 50,
        DateTime? from = null,
        DateTime? to = null)
    {
        var url = BuildUrl($"api/TokenUsage/history?page={page}&pageSize={pageSize}", from, to);
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PagedResult<TokenUsageDto>>();
    }

    private static string BuildUrl(string baseUrl, DateTime? from, DateTime? to)
    {
        var url = baseUrl;
        var separator = baseUrl.Contains('?') ? "&" : "?";

        if (from.HasValue)
        {
            url += $"{separator}from={from.Value:yyyy-MM-dd}";
            separator = "&";
        }

        if (to.HasValue)
        {
            url += $"{separator}to={to.Value:yyyy-MM-dd}";
        }

        return url;
    }
}
