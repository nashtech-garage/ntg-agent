using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.McpTools;

namespace NTG.Agent.MCP.Server.Tests.Services;

[TestFixture]
public class JsonSchemaToolsTests
{
    private readonly JsonSchemaTools tools = new();

    [Test]
    public void ToolMethods_AreRegisteredWithStableMcpNames()
    {
        Assert.That(
            typeof(JsonSchemaTools).GetCustomAttribute<McpServerToolTypeAttribute>(),
            Is.Not.Null);
        Assert.That(GetToolName(nameof(JsonSchemaTools.GenerateJsonSchema)), Is.EqualTo(JsonSchemaTools.JsonSchemaGenerateToolName));
        Assert.That(GetToolName(nameof(JsonSchemaTools.ValidateJsonSchema)), Is.EqualTo(JsonSchemaTools.JsonSchemaValidateToolName));
    }

    [Test]
    public void GenerateTool_UsesDraft202012AndIncludesOnlyRequiredProperties()
    {
        using var result = JsonDocument.Parse(tools.GenerateJsonSchema(
            "Product",
            [
                new("p1", "productId", "string", true),
                new("p2", "description", "string", false),
            ],
            CancellationToken.None));

        var output = result.RootElement;
        Assert.That(output.GetProperty("succeeded").GetBoolean(), Is.True);
        using var schema = JsonDocument.Parse(output.GetProperty("schemaJson").GetString()!);
        Assert.That(
            schema.RootElement.GetProperty("$schema").GetString(),
            Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
        Assert.That(schema.RootElement.GetProperty("required")[0].GetString(), Is.EqualTo("productId"));
        Assert.That(schema.RootElement.GetProperty("required").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public void GenerateTool_CreatesTypeCorrectSampleValues()
    {
        using var result = JsonDocument.Parse(tools.GenerateJsonSchema(
            "Example",
            [
                new("p1", "text", "string", true),
                new("p2", "amount", "number", true),
                new("p3", "count", "integer", true),
                new("p4", "enabled", "boolean", true),
                new("p5", "metadata", "object", false),
                new("p6", "items", "array", false),
                new("p7", "empty", "null", false),
            ],
            CancellationToken.None));

        Assert.That(result.RootElement.GetProperty("succeeded").GetBoolean(), Is.True);
        using var sample = JsonDocument.Parse(result.RootElement.GetProperty("sampleJson").GetString()!);
        var root = sample.RootElement;
        Assert.That(root.GetProperty("text").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("amount").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(root.GetProperty("count").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(root.GetProperty("enabled").ValueKind, Is.EqualTo(JsonValueKind.False));
        Assert.That(root.GetProperty("metadata").ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(root.GetProperty("items").ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(root.GetProperty("empty").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void GenerateTool_RejectsDuplicatePropertyNames()
    {
        using var result = JsonDocument.Parse(tools.GenerateJsonSchema(
            "Duplicate",
            [
                new("p1", "name", "string", true),
                new("p2", "name", "number", false),
            ],
            CancellationToken.None));

        Assert.That(result.RootElement.GetProperty("succeeded").GetBoolean(), Is.False);
        Assert.That(
            result.RootElement.GetProperty("errors")[0].GetString(),
            Is.EqualTo("Property name 'name' is duplicated."));
    }

    [Test]
    public void ValidateTool_AcceptsAndCanRevalidateTheSameSchema()
    {
        const string schema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object"}""";

        using var first = JsonDocument.Parse(tools.ValidateJsonSchema(schema, "{}", CancellationToken.None));
        using var second = JsonDocument.Parse(tools.ValidateJsonSchema(schema, "{}", CancellationToken.None));

        Assert.That(second.RootElement.GetProperty("instanceValid").GetBoolean(), Is.True);
        Assert.That(second.RootElement.GetProperty("report").GetString(), Does.Contain("Validation successful"));
    }

    [Test]
    public void ValidateTool_ReportsSchemaValidationErrors()
    {
        const string schema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "price": { "type": "number" } },
              "required": ["price"]
            }
            """;

        using var result = JsonDocument.Parse(tools.ValidateJsonSchema(
            schema,
            """{"price":"wrong"}""",
            CancellationToken.None));

        Assert.That(result.RootElement.GetProperty("instanceValid").GetBoolean(), Is.False);
        Assert.That(
            result.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
                error.GetProperty("keyword").GetString() == "type"
                && error.GetProperty("path").GetString() == "/price"),
            Is.True);
    }

    [Test]
    public void ValidateTool_DistinguishesInvalidJsonAndInvalidSchema()
    {
        using var invalidJson = JsonDocument.Parse(tools.ValidateJsonSchema(
            """{"type":"object"}""",
            """{"name":""",
            CancellationToken.None));
        using var invalidSchema = JsonDocument.Parse(tools.ValidateJsonSchema(
            """{"type":"not-a-json-type"}""",
            "{}",
            CancellationToken.None));

        Assert.That(invalidJson.RootElement.GetProperty("status").GetString(), Is.EqualTo("Invalid JSON syntax"));
        Assert.That(invalidSchema.RootElement.GetProperty("status").GetString(), Is.EqualTo("Invalid JSON Schema"));
    }

    private static string? GetToolName(string methodName) =>
        typeof(JsonSchemaTools)
            .GetMethod(methodName)!
            .GetCustomAttribute<McpServerToolAttribute>()?
            .Name;
}
