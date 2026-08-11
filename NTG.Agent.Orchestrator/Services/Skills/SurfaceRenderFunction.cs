using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// The <c>render_skill_surface</c> tool: draws one of a skill's bundled A2UI templates in the chat,
/// with model-supplied values merged into its data model.
/// </summary>
/// <remarks>
/// <para>
/// This is the "Path B" half of the A2UI integration. The model does not author a component tree —
/// it names a template that was authored by hand, validated against the real catalog at import, and
/// stored. That removes the entire class of failure where a model emits plausible-looking component
/// JSON using properties that do not exist, which is what the seven dead prop names in the render
/// guide caused for months.
/// </para>
/// <para>
/// Deliberately <em>not</em> built on <see cref="CapturingAIFunction"/>. That wrapper returns the
/// inner result to the model unchanged, which would dump the whole surface JSON back into context on
/// every render — hundreds of lines the model has no use for. Here the two audiences are split: the
/// browser gets the full operations through <see cref="RenderableToolCapture"/>, and the model gets a
/// one-line receipt.
/// </para>
/// <para>
/// The agent id is closed over at construction, never taken as a parameter. Every lookup re-checks
/// that the named skill is bound and enabled for that agent, so naming a skill belonging to another
/// agent returns nothing.
/// </para>
/// </remarks>
internal sealed class SurfaceRenderFunction : AIFunction
{
    /// <summary>Matches the schema shipped in <c>basic_catalog.json</c>.</summary>
    private const string CatalogId = "https://a2ui.org/specification/v0_9/basic_catalog.json";

    private const string ProtocolVersion = "v0.9";

    /// <summary>Templates live under <c>assets/</c>, per the spec's conventional layout.</summary>
    private const string AssetPrefix = "assets/";

    private readonly SkillRegistry _registry;
    private readonly RenderableToolCapture _capture;
    private readonly Guid _agentId;
    private readonly ILogger _logger;

    public SurfaceRenderFunction(
        SkillRegistry registry, RenderableToolCapture capture, Guid agentId, ILogger logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _agentId = agentId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => SkillPrompt.RenderToolName;

    public override string Description =>
        "Render one of a skill's interactive surfaces in the chat. The surface is a pre-built "
        + "template — you supply values to fill it in, you do not describe the UI. Render at most "
        + "one surface per reply, then stop and wait for the user to submit it.";

    public override JsonElement JsonSchema { get; } = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "skill": {
              "type": "string",
              "description": "Name of the skill that owns the surface, as listed in your available skills."
            },
            "surface": {
              "type": "string",
              "description": "Name of the surface template, without the assets/ prefix or .json suffix."
            },
            "values": {
              "type": "object",
              "description": "Values merged into the surface's data model, matching the shape the skill's instructions document. Omit to render the template's defaults."
            }
          },
          "required": ["skill", "surface"]
        }
        """);

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        new(RenderAsync(arguments, cancellationToken));

    private async Task<object?> RenderAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var skill = ReadString(arguments, "skill");
        var surface = ReadString(arguments, "surface");

        if (string.IsNullOrWhiteSpace(skill) || string.IsNullOrWhiteSpace(surface))
        {
            return Failure("Both 'skill' and 'surface' are required.");
        }

        // Models routinely pass "assets/trip-search.json" despite the schema saying otherwise.
        // Normalising is cheaper than a retry round-trip, and the path is not a lookup key beyond
        // this point — the registry query is scoped to the agent's own bindings.
        var assetPath = AssetPrefix + NormaliseSurfaceName(surface) + ".json";

        var content = await _registry.GetActiveSkillAssetAsync(_agentId, skill, assetPath, cancellationToken);

        if (content is null)
        {
            _logger.LogInformation(
                "Agent {AgentId} requested surface '{Surface}' from skill '{Skill}', which is not available",
                _agentId,
                assetPath,
                skill);

            return Failure(
                $"No surface named '{surface}' is available in skill '{skill}'. "
                + "Check the surface names in the skill's instructions.");
        }

        JsonNode? template;
        try
        {
            template = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            // Import validation should make this unreachable; if it happens the stored asset is
            // corrupt, which is an operator problem rather than something the model can fix.
            _logger.LogError(ex, "Stored surface '{Surface}' in skill '{Skill}' is not valid JSON", assetPath, skill);
            return Failure($"The surface '{surface}' could not be read.");
        }

        if (template is not JsonObject templateObject)
        {
            return Failure($"The surface '{surface}' is not a valid surface template.");
        }

        var surfaceId = templateObject["surfaceId"]?.GetValue<string>();
        var components = templateObject["components"] as JsonArray;

        // An empty component array would emit an updateComponents violating the schema's
        // minItems: 1. SurfaceValidator blocks it at import, so only rows predating the validator
        // can reach here — which is exactly why it is checked rather than assumed.
        if (string.IsNullOrWhiteSpace(surfaceId) || components is null || components.Count == 0)
        {
            return Failure($"The surface '{surface}' is missing its surfaceId or components.");
        }

        var data = templateObject["data"] as JsonObject ?? [];

        // Refuse rather than render a half-broken surface. A frozen input looks like a working
        // form that ignores the user, which is far worse than a message the model can act on and
        // retry — and unlike a rendered card, a refusal costs the user nothing.
        var mismatches = new List<string>();
        Merge(data, ReadValues(arguments), string.Empty, mismatches);

        if (mismatches.Count > 0)
        {
            _logger.LogInformation(
                "Agent {AgentId} sent values of the wrong shape for '{Surface}': {Mismatches}",
                _agentId,
                assetPath,
                string.Join("; ", mismatches));

            return Failure(
                $"The values did not match the shape '{surface}' expects: {string.Join("; ", mismatches)}. "
                + "Send the values nested exactly as the skill's instructions show, then try again.");
        }

        var operations = new JsonArray
        {
            new JsonObject
            {
                ["version"] = ProtocolVersion,
                ["createSurface"] = new JsonObject
                {
                    ["surfaceId"] = surfaceId,
                    ["catalogId"] = CatalogId,
                },
            },
            new JsonObject
            {
                ["version"] = ProtocolVersion,
                ["updateComponents"] = new JsonObject
                {
                    ["surfaceId"] = surfaceId,
                    ["components"] = components.DeepClone(),
                },
            },
        };

        // One update per top-level branch, rather than a single write to "/".
        //
        // Writing the root REPLACES the entire data model, and the model holds more than the
        // template seeds: the catalog overrides mirror every input the user touches to
        // /__inputs/<componentId>, which is what a Button's formData carries back to us. Replacing
        // the root on a re-render therefore discards the user's own answers while the inputs on
        // screen still show them — a surface that looks correct and has quietly lost its data.
        //
        // Per-branch writes cost nothing on a first render, where the model is empty and each key
        // is created exactly as a root write would have created it, and they leave every branch we
        // do not own untouched on every render after that.
        foreach (var branch in data)
        {
            operations.Add(new JsonObject
            {
                ["version"] = ProtocolVersion,
                ["updateDataModel"] = new JsonObject
                {
                    ["surfaceId"] = surfaceId,
                    ["path"] = "/" + branch.Key,
                    ["value"] = branch.Value?.DeepClone(),
                },
            });
        }

        var payload = new JsonObject { ["a2ui_operations"] = operations }.ToJsonString();

        _capture.Add(new CapturedToolCall(
            Guid.NewGuid().ToString(),
            Name,
            new Dictionary<string, object?> { ["skill"] = skill, ["surface"] = surface },
            payload));

        _logger.LogInformation(
            "Agent {AgentId} rendered surface '{Surface}' from skill '{Skill}' ({ComponentCount} components)",
            _agentId,
            assetPath,
            skill,
            components.Count);

        // The receipt, not the payload. Everything the browser needs already went to the capture.
        return $"Rendered the '{surface}' surface. Do not describe it — the user can see it. "
               + "Stop now and wait for them to submit it.";
    }

    /// <summary>Accepts <c>trip-search</c>, <c>trip-search.json</c> and <c>assets/trip-search.json</c>.</summary>
    private static string NormaliseSurfaceName(string surface)
    {
        var name = surface.Trim();

        if (name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[AssetPrefix.Length..];
        }

        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        return name;
    }

    /// <summary>
    /// Recursively merges <paramref name="incoming"/> into <paramref name="target"/>. Objects merge
    /// key by key; every other kind replaces wholesale, so a model sending a shorter list replaces
    /// the list rather than leaving stale trailing entries behind.
    /// </summary>
    private static void Merge(JsonObject target, JsonObject? incoming, string path, List<string> mismatches)
    {
        if (incoming is null)
        {
            return;
        }

        foreach (var property in incoming.ToList())
        {
            var here = $"{path}/{property.Key}";
            var existing = target[property.Key];

            // A seeded path may be overwritten, but not have its *kind* changed. The surface
            // validator guarantees every binding has a seed; swapping an object for a scalar
            // removes every path beneath it, so the inputs bound there render frozen — exactly
            // the failure the validator exists to prevent, reintroduced at run time by the model,
            // past the point validation reaches. Same for an array: ChoicePicker binds one even in
            // single-select mode, and a bare string in its place stops it resolving.
            if (existing is not null && KindOf(existing) != KindOf(property.Value))
            {
                mismatches.Add(
                    $"'{here}' expects {Describe(existing)} but received {KindOf(property.Value).ToString().ToLowerInvariant()}");
                continue;
            }

            if (property.Value is JsonObject nested && existing is JsonObject target2)
            {
                Merge(target2, nested, here, mismatches);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private enum ValueKind
    {
        Object,
        Array,
        Scalar,
    }

    private static ValueKind KindOf(JsonNode? node) => node switch
    {
        JsonObject => ValueKind.Object,
        JsonArray => ValueKind.Array,
        _ => ValueKind.Scalar,
    };

    /// <summary>
    /// Names the expected shape well enough for the model to correct itself. For an object that
    /// means listing its keys — "an object with keys destination, departDate" is actionable in a
    /// way that "an object" is not.
    /// </summary>
    private static string Describe(JsonNode node) => node switch
    {
        JsonObject o when o.Count > 0 => $"an object with keys {string.Join(", ", o.Select(p => p.Key))}",
        JsonObject => "an object",
        JsonArray => "an array",
        _ => "a single value",
    };

    private static JsonObject? ReadValues(AIFunctionArguments arguments)
    {
        if (arguments is null || !arguments.TryGetValue("values", out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            JsonObject direct => direct,
            JsonElement element when element.ValueKind == JsonValueKind.Object =>
                JsonNode.Parse(element.GetRawText()) as JsonObject,
            // Some providers hand structured arguments back as a JSON string.
            string text when !string.IsNullOrWhiteSpace(text) => TryParseObject(text),
            _ => JsonSerializer.SerializeToNode(raw) as JsonObject,
        };
    }

    private static JsonObject? TryParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(AIFunctionArguments arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => raw.ToString(),
        };
    }

    /// <summary>
    /// Failures are returned to the model as text rather than thrown. A throw aborts the run; a
    /// message lets the model correct itself or fall back to answering in prose.
    /// </summary>
    private static string Failure(string message) => message;
}
