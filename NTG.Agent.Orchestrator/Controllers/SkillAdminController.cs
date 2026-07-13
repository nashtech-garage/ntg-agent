using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos.Skills;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Admin-only proxy for the skill catalog CRUD API hosted on the MCP server.
/// The MCP server is internal-only; this controller is the authorization boundary.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class SkillAdminController : ControllerBase
{
    /// <summary>Named HttpClient whose BaseAddress points at the MCP server (Aspire service discovery).</summary>
    public const string McpSkillCatalogClientName = "McpSkillCatalog";

    private readonly IHttpClientFactory _httpClientFactory;

    public SkillAdminController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    [HttpGet]
    public Task<IActionResult> GetSkills() => ForwardAsync(client => client.GetAsync("api/skills"));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetSkill(Guid id) => ForwardAsync(client => client.GetAsync($"api/skills/{id}"));

    [HttpPost]
    public Task<IActionResult> CreateSkill([FromBody] SaveSkillRequest request)
        => ForwardAsync(client => client.PostAsJsonAsync("api/skills", request));

    [HttpPut("{id:guid}")]
    public Task<IActionResult> UpdateSkill(Guid id, [FromBody] SaveSkillRequest request)
        => ForwardAsync(client => client.PutAsJsonAsync($"api/skills/{id}", request));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> DeleteSkill(Guid id) => ForwardAsync(client => client.DeleteAsync($"api/skills/{id}"));

    private async Task<IActionResult> ForwardAsync(Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        var client = _httpClientFactory.CreateClient(McpSkillCatalogClientName);
        using var response = await send(client);
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = response.Content.Headers.ContentType?.ToString(),
        };
    }
}
