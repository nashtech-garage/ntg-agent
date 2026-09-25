using NTG.Agent.Common.Dtos.Agents;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

/// <summary>
/// Calls the admin backend to test provider credentials and fetch the live model
/// list. The backend forwards the key to the provider (the browser cannot, due to
/// CORS and key exposure), mirroring <see cref="AgentAccessClient"/>'s style.
/// </summary>
public class ProviderProbeClient(HttpClient httpClient)
{
    public async Task<ProviderTestResult> TestConnectionAsync(ProviderProbeRequest request)
    {
        var res = await httpClient.PostAsJsonAsync("api/agentadmin/provider/test-connection", request);
        if (!res.IsSuccessStatusCode)
        {
            var message = await res.Content.ReadAsStringAsync();
            return new ProviderTestResult(false, string.IsNullOrWhiteSpace(message) ? "Connection test failed." : message);
        }
        return (await res.Content.ReadFromJsonAsync<ProviderTestResult>())
            ?? new ProviderTestResult(false, "Connection test failed.");
    }

    public async Task<List<string>> FetchModelsAsync(ProviderProbeRequest request)
    {
        var res = await httpClient.PostAsJsonAsync("api/agentadmin/provider/models", request);
        if (!res.IsSuccessStatusCode)
        {
            var message = await res.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Failed to fetch models." : message);
        }
        var payload = await res.Content.ReadFromJsonAsync<ProviderModelsResponse>();
        return payload?.Models ?? [];
    }
}
