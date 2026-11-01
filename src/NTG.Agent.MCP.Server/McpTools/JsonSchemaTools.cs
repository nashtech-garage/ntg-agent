using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using ModelContextProtocol.Server;

namespace NTG.Agent.MCP.Server.McpTools;

[McpServerToolType]
public sealed class JsonSchemaTools
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

    private static readonly HashSet<string> SupportedSchemaTypes =
        new(["string", "number", "integer", "boolean", "object", "array", "null"], StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    private const string Draft202012Schema = "https://json-schema.org/draft/2020-12/schema";

    public sealed record JsonSchemaPropertyDefinition(string Id, string Name, string Type, bool Required);

    [McpServerTool(Name = JsonSchemaGenerateToolName)]
    [Description(
        "Generate a Draft 2020-12 JSON Schema and matching sample JSON from a title and property rows. "
        + "Returns structured errors when the design is invalid.")]
    public string GenerateJsonSchema(
        [Description("The title to place in the generated JSON Schema.")] string schemaName,
        [Description("Property rows, including name, type, required, and stable id.")] JsonSchemaPropertyDefinition[] properties,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GenerateJsonSchemaResult(schemaName, properties);
    }

    [McpServerTool(Name = JsonSchemaValidateToolName)]
    [Description(
        "Deterministically validate a JSON Schema and JSON instance. Returns schema validity, "
        + "JSON parsing status, instance validity, and structured validation errors.")]
    public string ValidateJsonSchema(
        [Description("The JSON Schema to validate.")] string schemaJson,
        [Description("The JSON instance text to validate against the schema.")] string sampleJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValidateJsonSchemaResult(schemaJson, sampleJson);
    }

    private static string GenerateJsonSchemaResult(
        string schemaName,
        JsonSchemaPropertyDefinition[] properties)
    {
        var propertyRows = properties ?? [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            errors.Add("Schema name cannot be empty.");
        }

        if (propertyRows.Length == 0)
        {
            errors.Add("Add at least one property before generating the schema.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in propertyRows)
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

        foreach (var property in propertyRows)
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

    private static string ValidateJsonSchemaResult(string schemaJson, string sampleJson)
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
