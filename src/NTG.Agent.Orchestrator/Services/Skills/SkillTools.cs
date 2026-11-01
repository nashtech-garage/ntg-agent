using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Builds the per-request skill tools handed to a running agent.
/// </summary>
/// <remarks>
/// The load and render tools are constructed per request and close over the agent id, so the agent
/// a run belongs to is never a model-supplied parameter. Built-in utilities are stateless platform
/// capabilities and are available to any agent that has an active skill.
/// </remarks>
public static class SkillTools
{
    public const string JsonSchemaGenerateToolName = "json_schema_generate";
    public const string JsonSchemaValidateToolName = "json_schema_validate";

    private static readonly JsonSerializerOptions ToolJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        MaxDepth = 32,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Input shape shared by the generic JSON Schema generation tool.</summary>
    public sealed record JsonSchemaPropertyDefinition(string Id, string Name, string Type, bool Required);

    /// <summary>
    /// The tier-2 tool. Returns a skill's full <c>SKILL.md</c> body so the model can follow it.
    /// </summary>
    public static AIFunction CreateLoadSkill(
        SkillRegistry registry, SkillActivityLog activityLog, Guid agentId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(activityLog);
        ArgumentNullException.ThrowIfNull(logger);

        return AIFunctionFactory.Create(
            async ([Description("Name of the skill to read, exactly as listed in your available skills.")] string skill,
                   CancellationToken cancellationToken) =>
            {
                var body = await registry.GetActiveSkillBodyAsync(agentId, skill, cancellationToken);

                if (body is null)
                {
                    logger.LogInformation(
                        "Agent {AgentId} tried to load skill '{SkillName}', which is not bound or not enabled",
                        agentId,
                        skill);

                    return $"No skill named '{skill}' is available to you. "
                           + "Use one of the names listed in the available skills section, or answer without a skill.";
                }

                logger.LogInformation("Agent {AgentId} loaded skill '{SkillName}'", agentId, skill);
                // Narrate only a successful load — a refused one named a skill the agent never
                // actually used, and telling the user otherwise would misrepresent the run.
                activityLog.Add($"Using the {skill} skill.");
                return body;
            },
            name: SkillPrompt.LoadToolName,
            description:
                "Read the full instructions for one of your available skills. Call this with the "
                + "skill's name before acting on it; the catalog description alone is not enough to "
                + "work from.");
    }

    /// <summary>The tier-3 tool: renders one of a skill's bundled A2UI surface templates.</summary>
    public static AIFunction CreateRenderSurface(
        SkillRegistry registry,
        Agents.RenderableToolCapture capture,
        SkillActivityLog activityLog,
        Guid agentId,
        ILogger logger) =>
        new SurfaceRenderFunction(registry, capture, activityLog, agentId, logger);

    /// <summary>
    /// Creates platform tools available to any agent with an active skill. Skills choose whether
    /// to use these tools through their database-stored instructions; no skill-name registration
    /// is required.
    /// </summary>
    public static IReadOnlyList<AITool> CreateBuiltInTools() =>
    [
        CreateJsonSchemaGenerateTool(),
        CreateJsonSchemaValidateTool(),
    ];

    private static AIFunction CreateJsonSchemaGenerateTool() =>
        AIFunctionFactory.Create(
            ([Description("The title to place in the generated JSON Schema.")] string schemaName,
             [Description("Property rows, including name, type, required, and stable id.")] JsonSchemaPropertyDefinition[] properties,
             CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return GenerateJsonSchema(schemaName, properties);
            },
            name: JsonSchemaGenerateToolName,
            description:
                "Generate a Draft 2020-12 JSON Schema and matching sample JSON from a title and property rows. "
                + "Returns structured errors when the design is invalid.");

    private static AIFunction CreateJsonSchemaValidateTool() =>
        AIFunctionFactory.Create(
            ([Description("The JSON Schema to validate.")] string schemaJson,
             [Description("The JSON instance text to validate against the schema.")] string sampleJson,
             CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValidateJsonSchema(schemaJson, sampleJson);
            },
            name: JsonSchemaValidateToolName,
            description:
                "Deterministically validate a JSON Schema and JSON instance. Returns schema validity, "
                + "JSON parsing status, instance validity, and structured validation errors.");

    private static string GenerateJsonSchema(
        string schemaName,
        IReadOnlyList<JsonSchemaPropertyDefinition> properties)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            errors.Add("Schema name cannot be empty.");
        }

        if (properties is null || properties.Count == 0)
        {
            errors.Add("Add at least one property before generating the schema.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties ?? [])
        {
            if (property is null)
            {
                errors.Add("Property entries cannot be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(property.Name))
            {
                errors.Add("Property names cannot be empty.");
            }
            else if (!names.Add(property.Name))
            {
                errors.Add($"Property name '{property.Name}' is duplicated.");
            }

            if (!SupportedSchemaTypes.Contains(property.Type))
            {
                errors.Add($"Property '{property.Name}' has unsupported data type '{property.Type}'.");
            }
        }

        if (errors.Count > 0)
        {
            return JsonSerializer.Serialize(
                new { succeeded = false, schemaJson = (string?)null, sampleJson = (string?)null, errors },
                ToolJsonOptions);
        }

        var schemaProperties = new JsonObject();
        var sample = new JsonObject();
        var required = new JsonArray();

        foreach (var property in properties)
        {
            schemaProperties[property.Name] = new JsonObject { ["type"] = property.Type };
            sample[property.Name] = SampleValue(property.Type);
            if (property.Required)
            {
                required.Add(property.Name);
            }
        }

        var schema = new JsonObject
        {
            ["$schema"] = Draft202012Schema,
            ["title"] = schemaName.Trim(),
            ["type"] = "object",
            ["properties"] = schemaProperties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.Serialize(
            new
            {
                succeeded = true,
                schemaJson = schema.ToJsonString(JsonOutputOptions),
                sampleJson = sample.ToJsonString(JsonOutputOptions),
                errors = Array.Empty<string>(),
            },
            ToolJsonOptions);
    }

    private static string ValidateJsonSchema(string schemaJson, string sampleJson)
    {
        var errors = new List<JsonSchemaValidationError>();
        JsonSchema? schema = null;
        var schemaValid = false;
        var jsonValid = false;
        var evaluationValid = false;
        JsonDocument? sampleDocument = null;

        try
        {
            using var schemaDocument = JsonDocument.Parse(schemaJson, JsonDocumentOptions);
            schema = JsonSchema.Build(
                schemaDocument.RootElement.Clone(),
                new BuildOptions
                {
                    Dialect = Dialect.Draft202012,
                    SchemaRegistry = new SchemaRegistry(),
                    DialectRegistry = new DialectRegistry(),
                    VocabularyRegistry = new VocabularyRegistry(),
                });
            schemaValid = true;
        }
        catch (JsonException)
        {
            errors.Add(new JsonSchemaValidationError("$", "schema", "The JSON Schema must be valid JSON."));
        }
        catch (ArgumentException)
        {
            errors.Add(new JsonSchemaValidationError(
                "$",
                "schema",
                "The JSON Schema is not valid Draft 2020-12."));
        }
        catch (JsonSchemaException)
        {
            errors.Add(new JsonSchemaValidationError(
                "$",
                "schema",
                "The JSON Schema is not valid Draft 2020-12."));
        }
        catch (InvalidOperationException)
        {
            errors.Add(new JsonSchemaValidationError(
                "$",
                "schema",
                "The JSON Schema is not valid Draft 2020-12."));
        }

        try
        {
            sampleDocument = JsonDocument.Parse(sampleJson, JsonDocumentOptions);
            jsonValid = true;
        }
        catch (JsonException)
        {
            errors.Add(new JsonSchemaValidationError("$", "syntax", "The sample is not valid JSON."));
        }

        try
        {
            if (schemaValid && jsonValid && schema is not null && sampleDocument is not null)
            {
                var evaluation = schema.Evaluate(
                    sampleDocument.RootElement,
                    new EvaluationOptions { OutputFormat = OutputFormat.List });
                evaluationValid = evaluation.IsValid;
                CollectErrors(evaluation, errors);
            }
        }
        finally
        {
            sampleDocument?.Dispose();
        }

        var instanceValid = schemaValid && jsonValid && evaluationValid;
        var status = !schemaValid
            ? "Invalid JSON Schema"
            : !jsonValid
                ? "Invalid JSON syntax"
                : instanceValid
                    ? "Validation successful"
                    : "Instance validation failed";
        var reportLines = new List<string>
        {
            $"Status: {status}",
            $"Schema: {(schemaValid ? "valid" : "invalid")}",
            $"JSON syntax: {(jsonValid ? "valid" : "invalid")}",
            $"Instance: {(instanceValid ? "valid" : "invalid")}",
            $"Error count: {errors.Count}",
        };
        reportLines.AddRange(errors.Select(error => $"{error.Path} ({error.Keyword}): {error.Message}"));

        return JsonSerializer.Serialize(
            new
            {
                status,
                schemaValid,
                jsonValid,
                instanceValid,
                errorCount = errors.Count,
                errors,
                report = string.Join(Environment.NewLine, reportLines),
            },
            ToolJsonOptions);
    }

    private static readonly HashSet<string> SupportedSchemaTypes =
        new(["string", "number", "integer", "boolean", "object", "array", "null"], StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    private const string Draft202012Schema = "https://json-schema.org/draft/2020-12/schema";

    private static JsonNode? SampleValue(string type) =>
        type switch
        {
            "string" => JsonValue.Create("string"),
            "number" => JsonValue.Create(0),
            "integer" => JsonValue.Create(0),
            "boolean" => JsonValue.Create(false),
            "object" => new JsonObject(),
            "array" => new JsonArray(),
            "null" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported JSON Schema type."),
        };

    private static void CollectErrors(
        EvaluationResults result,
        List<JsonSchemaValidationError> errors)
    {
        if (result.Errors is not null)
        {
            var path = result.InstanceLocation.ToString();
            if (string.IsNullOrEmpty(path))
            {
                path = "$";
            }

            foreach (var (keyword, message) in result.Errors)
            {
                errors.Add(new JsonSchemaValidationError(path, keyword, message));
            }
        }

        if (result.Details is null)
        {
            return;
        }

        foreach (var detail in result.Details)
        {
            CollectErrors(detail, errors);
        }
    }

    private sealed record JsonSchemaValidationError(string Path, string Keyword, string Message);
}
