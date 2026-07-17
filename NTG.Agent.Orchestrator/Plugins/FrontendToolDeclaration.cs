using Microsoft.Extensions.AI;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Plugins;

/// <summary>
/// Declaration-only tool representing an AG-UI frontend tool that executes in the browser.
/// Because this is an <see cref="AIFunctionDeclaration"/> rather than an invocable
/// <see cref="AIFunction"/>, the function-invoking pipeline surfaces the model's call to the
/// caller (as <see cref="FunctionCallContent"/>) instead of executing it server-side.
/// </summary>
public sealed class FrontendToolDeclaration : AIFunctionDeclaration
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;

    public FrontendToolDeclaration(string name, string? description, JsonElement parametersSchema)
    {
        _name = name;
        _description = description ?? string.Empty;
        _jsonSchema = parametersSchema;
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _jsonSchema;

    private static readonly JsonElement EmptyObjectSchema =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    /// <summary>
    /// Parses the CopilotKit frontend tools JSON array ([{name, description, parameters}, ...],
    /// where parameters is a JSON Schema object) into declaration-only tools.
    /// </summary>
    public static List<AITool> ParseFromJson(string frontendToolsJson)
    {
        var tools = new List<AITool>();
        using var doc = JsonDocument.Parse(frontendToolsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return tools;

        foreach (var tool in doc.RootElement.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object))
        {
            var name = tool.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var description = tool.TryGetProperty("description", out var d) ? d.GetString() : null;
            // Clone so the schema stays valid after the JsonDocument is disposed
            var parameters = tool.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
                ? p.Clone()
                : EmptyObjectSchema;

            tools.Add(new FrontendToolDeclaration(name, description, parameters));
        }

        return tools;
    }
}
