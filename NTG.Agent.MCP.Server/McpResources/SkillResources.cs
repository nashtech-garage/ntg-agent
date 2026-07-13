using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Data;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NTG.Agent.MCP.Server.McpResources;

/// <summary>
/// Exposes stored Agent Skills over MCP using the skill:// resource convention (SEP-2640):
/// a skill://index.json discovery document plus one skill://{name}/SKILL.md resource per skill.
/// Consumed by Microsoft Agent Framework's UseMcpSkills / AgentSkillsProvider.
/// </summary>
[McpServerResourceType]
public sealed class SkillResources
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IDbContextFactory<SkillDbContext> _dbContextFactory;

    public SkillResources(IDbContextFactory<SkillDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("Discovery document listing the Agent Skills available on this server.")]
    public async Task<string> GetSkillIndex(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entries = await dbContext.Skills
            .OrderBy(s => s.Name)
            .Select(s => new SkillIndexEntry(s.Name, s.Description))
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new SkillIndex(entries), JsonOptions);
    }

    [McpServerResource(UriTemplate = "skill://{name}/SKILL.md", Name = "Skill Definition", MimeType = "text/markdown")]
    [Description("Full SKILL.md document (instructions) for a single Agent Skill.")]
    public async Task<string> GetSkillContent(string name, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var skill = await dbContext.Skills.FirstOrDefaultAsync(s => s.Name == name, cancellationToken)
            ?? throw new McpException($"Skill '{name}' was not found.");

        return skill.Content;
    }

    private sealed record SkillIndex(IReadOnlyList<SkillIndexEntry> Skills);

    private sealed record SkillIndexEntry(string Name, string Description)
    {
        public string Type => "skill-md";

        public string Url => $"skill://{Name}/SKILL.md";
    }
}
