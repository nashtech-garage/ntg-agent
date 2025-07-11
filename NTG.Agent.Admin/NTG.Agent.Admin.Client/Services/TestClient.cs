namespace NTG.Agent.Admin.Client.Services;

public class TestClient(HttpClient httpClient)
{
    public async Task<string> Test()
    {

        var response = await httpClient.GetAsync("api/test");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        return result;
    }
}
