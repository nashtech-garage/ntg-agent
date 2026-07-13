using NTG.Agent.Common.Dtos.Skills;
using System.Net.Http.Json;

namespace NTG.Agent.Admin.Client.Services;

public class SkillClient(HttpClient httpClient)
{
    public async Task<IList<SkillDto>> GetSkillsAsync()
    {
        var response = await httpClient.GetAsync("api/skilladmin");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<SkillDto>>();
        return result ?? [];
    }

    public async Task<SkillDetailDto?> GetSkillAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/skilladmin/{id}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SkillDetailDto>();
    }

    public async Task<SkillDetailDto> CreateSkillAsync(string content)
    {
        var response = await httpClient.PostAsJsonAsync("api/skilladmin", new SaveSkillRequest { Content = content });
        await EnsureSuccessWithMessageAsync(response);

        return (await response.Content.ReadFromJsonAsync<SkillDetailDto>())!;
    }

    public async Task<SkillDetailDto> UpdateSkillAsync(Guid id, string content)
    {
        var response = await httpClient.PutAsJsonAsync($"api/skilladmin/{id}", new SaveSkillRequest { Content = content });
        await EnsureSuccessWithMessageAsync(response);

        return (await response.Content.ReadFromJsonAsync<SkillDetailDto>())!;
    }

    public async Task DeleteSkillAsync(Guid id)
    {
        var response = await httpClient.DeleteAsync($"api/skilladmin/{id}");
        await EnsureSuccessWithMessageAsync(response);
    }

    // Validation failures (400/409) carry a human-readable reason in the body; surface it
    // instead of the generic EnsureSuccessStatusCode message.
    private static async Task EnsureSuccessWithMessageAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(string.IsNullOrWhiteSpace(message)
                ? $"Request failed with status {(int)response.StatusCode}."
                : message.Trim('"'));
        }
    }
}
