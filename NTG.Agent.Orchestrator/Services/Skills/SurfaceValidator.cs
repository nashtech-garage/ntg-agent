using System.Text.Json;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Validates an A2UI surface template — the hand-authored JSON a skill bundles under
/// <c>assets/</c> and that <c>render_skill_surface</c> turns into <c>a2ui_operations</c>.
/// </summary>
/// <remarks>
/// <para>
/// Run at import time so a malformed template is a rejected upload with a line-item error list,
/// rather than a blank card during a demo. Every check here corresponds to a way a surface can
/// silently fail to render: an unresolvable child reference draws nothing, an unknown property is
/// dropped by the catalog, and — the subtle one — an input bound to a data path that was never
/// seeded renders frozen, because A2UI inputs are controlled components whose setter is a no-op
/// until the path exists.
/// </para>
/// <para>
/// Returns <em>all</em> failures rather than the first: a skill author fixing one error per upload
/// round-trip will start disabling checks instead.
/// </para>
/// </remarks>
public static class SurfaceValidator
{
    /// <summary>
    /// Bounds recursion while parsing. Untrusted uploads can nest arbitrarily deeply, and the
    /// default limit of 64 is more than any legitimate surface needs.
    /// </summary>
    public const int MaxJsonDepth = 32;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = MaxJsonDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static IReadOnlyList<string> Validate(string surfaceName, string json)
    {
        var errors = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            // Covers depth overflow too: JsonException carries the reason and position.
            return [$"{surfaceName}: not valid JSON — {ex.Message}"];
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [$"{surfaceName}: top level must be a JSON object"];
            }

            ValidateSurfaceId(surfaceName, root, errors);

            if (!root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{surfaceName}: missing required 'components' array");
                return errors;
            }

            if (components.GetArrayLength() == 0)
            {
                errors.Add($"{surfaceName}: 'components' is empty");
                return errors;
            }

            var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : default;

            var byId = IndexComponents(surfaceName, components, errors);
            if (byId.Count == 0)
            {
                return errors;
            }

            var referenced = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (id, component) in byId)
            {
                ValidateComponent(surfaceName, id, component, byId, referenced, data, errors);
            }

            ValidateRoot(surfaceName, byId, errors);
            ValidateReachability(surfaceName, byId, referenced, errors);
        }

        return errors;
    }

    private static void ValidateSurfaceId(string surfaceName, JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("surfaceId", out var surfaceId)
            || surfaceId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(surfaceId.GetString()))
        {
            errors.Add($"{surfaceName}: missing or empty 'surfaceId'");
        }
    }

    private static Dictionary<string, JsonElement> IndexComponents(
        string surfaceName, JsonElement components, List<string> errors)
    {
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var index = 0;

        foreach (var component in components.EnumerateArray())
        {
            var position = index++;

            if (component.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{surfaceName}: components[{position}] is not an object");
                continue;
            }

            if (!component.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                errors.Add($"{surfaceName}: components[{position}] has no non-empty string 'id'");
                continue;
            }

            var id = idElement.GetString()!;
            if (!byId.TryAdd(id, component))
            {
                errors.Add($"{surfaceName}: duplicate component id '{id}'");
            }
        }

        return byId;
    }

    private static void ValidateComponent(
        string surfaceName,
        string id,
        JsonElement component,
        Dictionary<string, JsonElement> byId,
        HashSet<string> referenced,
        JsonElement data,
        List<string> errors)
    {
        if (!component.TryGetProperty("component", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{surfaceName}: '{id}' has no string 'component' property");
            return;
        }

        var type = typeElement.GetString()!;
        if (!A2uiCatalog.TryGet(type, out var spec))
        {
            errors.Add($"{surfaceName}: '{id}' uses unknown component '{type}'");
            return;
        }

        foreach (var required in spec.RequiredProperties)
        {
            if (!component.TryGetProperty(required, out _))
            {
                errors.Add($"{surfaceName}: '{id}' ({type}) is missing required property '{required}'");
            }
        }

        foreach (var property in component.EnumerateObject())
        {
            if (property.NameEquals("component"))
            {
                continue;
            }

            if (!spec.Properties.Contains(property.Name))
            {
                errors.Add($"{surfaceName}: '{id}' ({type}) has unknown property '{property.Name}'");
                continue;
            }

            // Enumerated props accept a literal from the set, or a binding resolved at runtime.
            if (spec.EnumeratedValues.TryGetValue(property.Name, out var allowed)
                && property.Value.ValueKind == JsonValueKind.String
                && !allowed.Contains(property.Value.GetString()!))
            {
                errors.Add(
                    $"{surfaceName}: '{id}' ({type}) has {property.Name}='{property.Value.GetString()}', " +
                    $"expected one of {string.Join('|', allowed)}");
            }

            if (A2uiCatalog.ChildReferenceProperties.Contains(property.Name))
            {
                ValidateChildReferences(surfaceName, id, property.Value, byId, referenced, errors);
            }
        }

        ValidateBindings(surfaceName, id, component, data, errors);
    }

    private static void ValidateChildReferences(
        string surfaceName,
        string id,
        JsonElement value,
        Dictionary<string, JsonElement> byId,
        HashSet<string> referenced,
        List<string> errors)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                Reference(value.GetString()!);
                break;

            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray())
                {
                    if (child.ValueKind == JsonValueKind.String)
                    {
                        Reference(child.GetString()!);
                    }
                    else
                    {
                        errors.Add($"{surfaceName}: '{id}' lists a child that is not a component id string");
                    }
                }

                break;

            // ChildList template form: { componentId, path } generates children from a data list.
            case JsonValueKind.Object when value.TryGetProperty("componentId", out var template)
                                           && template.ValueKind == JsonValueKind.String:
                Reference(template.GetString()!);
                break;

            default:
                errors.Add($"{surfaceName}: '{id}' has a child reference that is neither an id, a list of ids, nor a template");
                break;
        }

        void Reference(string childId)
        {
            referenced.Add(childId);

            if (string.Equals(childId, id, StringComparison.Ordinal))
            {
                errors.Add($"{surfaceName}: '{id}' references itself as a child");
            }
            else if (!byId.ContainsKey(childId))
            {
                errors.Add($"{surfaceName}: '{id}' references child '{childId}', which does not exist");
            }
        }
    }

    /// <summary>
    /// Every <c>{ "path": "/..." }</c> binding anywhere in the component — including nested ones
    /// inside <c>action.event.context</c> and <c>options[].label</c> — must have a seed in
    /// <c>data</c>. An unseeded path renders the input frozen and resolves button context to null.
    /// </summary>
    private static void ValidateBindings(
        string surfaceName, string id, JsonElement component, JsonElement data, List<string> errors)
    {
        Walk(component);

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object when TryReadBinding(element, out var path):
                    if (!HasSeed(data, path))
                    {
                        errors.Add($"{surfaceName}: '{id}' binds '{path}', which has no seed in 'data'");
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    private static bool TryReadBinding(JsonElement element, out string path)
    {
        path = string.Empty;

        // A binding is exactly { "path": "..." }. An object that merely contains a "path"
        // alongside other keys is a ChildList template, handled separately.
        var properties = element.EnumerateObject();
        var count = 0;
        string? candidate = null;

        foreach (var property in properties)
        {
            count++;
            if (count > 1)
            {
                return false;
            }

            if (!property.NameEquals("path") || property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            candidate = property.Value.GetString();
        }

        if (count != 1 || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static bool HasSeed(JsonElement data, string path)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var current = data;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateRoot(string surfaceName, Dictionary<string, JsonElement> byId, List<string> errors)
    {
        if (!byId.TryGetValue("root", out var root))
        {
            errors.Add($"{surfaceName}: no component with id 'root'");
            return;
        }

        if (root.TryGetProperty("component", out var type)
            && type.ValueKind == JsonValueKind.String
            && !A2uiCatalog.LayoutComponents.Contains(type.GetString()!))
        {
            errors.Add(
                $"{surfaceName}: root is '{type.GetString()}', which cannot lay out children — " +
                $"use one of {string.Join('|', A2uiCatalog.LayoutComponents)}");
        }
    }

    private static void ValidateReachability(
        string surfaceName, Dictionary<string, JsonElement> byId, HashSet<string> referenced, List<string> errors)
    {
        foreach (var id in byId.Keys.Where(id => id != "root" && !referenced.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"{surfaceName}: '{id}' is never referenced and will not render");
        }
    }
}
