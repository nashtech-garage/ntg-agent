using System.Text.Json;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Skills;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

[TestFixture]
public class SkillToolsTests
{
    private static AIFunction GetTool(string name) =>
        SkillTools.CreateBuiltInTools()
            .OfType<AIFunction>()
            .Single(tool => tool.Name == name);

    private static async Task<JsonDocument> InvokeToolAsync(string name, AIFunctionArguments arguments)
    {
        var result = await GetTool(name).InvokeAsync(arguments);
        var json = result switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString()!,
            JsonElement element => element.GetRawText(),
            string text => text,
            _ => throw new AssertionException($"Unexpected tool result type: {result?.GetType().Name ?? "null"}"),
        };
        return JsonDocument.Parse(json);
    }

    [Test]
    public void CreateBuiltInTools_ExposesTheSharedSchemaTools()
    {
        var names = SkillTools.CreateBuiltInTools().OfType<AIFunction>().Select(tool => tool.Name);

        Assert.That(
            names,
            Is.EquivalentTo([
                SkillTools.JsonSchemaGenerateToolName,
                SkillTools.JsonSchemaValidateToolName,
            ]));
    }

    [Test]
    public async Task GenerateTool_UsesDraft202012AndIncludesOnlyRequiredProperties()
    {
        using var result = await InvokeToolAsync(
            SkillTools.JsonSchemaGenerateToolName,
            new AIFunctionArguments
            {
                ["schemaName"] = "Product",
                ["properties"] = new[]
                {
                    new SkillTools.JsonSchemaPropertyDefinition("p1", "productId", "string", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p2", "description", "string", false),
                },
            });

        var output = result.RootElement;
        Assert.That(output.GetProperty("succeeded").GetBoolean(), Is.True);
        using var schema = JsonDocument.Parse(output.GetProperty("schemaJson").GetString()!);
        Assert.That(
            schema.RootElement.GetProperty("$schema").GetString(),
            Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
        Assert.That(
            schema.RootElement.GetProperty("required")[0].GetString(),
            Is.EqualTo("productId"));
        Assert.That(schema.RootElement.GetProperty("required").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task GenerateTool_CreatesTypeCorrectSampleValues()
    {
        using var result = await InvokeToolAsync(
            SkillTools.JsonSchemaGenerateToolName,
            new AIFunctionArguments
            {
                ["schemaName"] = "Example",
                ["properties"] = new[]
                {
                    new SkillTools.JsonSchemaPropertyDefinition("p1", "text", "string", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p2", "amount", "number", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p3", "count", "integer", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p4", "enabled", "boolean", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p5", "metadata", "object", false),
                    new SkillTools.JsonSchemaPropertyDefinition("p6", "items", "array", false),
                    new SkillTools.JsonSchemaPropertyDefinition("p7", "empty", "null", false),
                },
            });

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
    public async Task GenerateTool_RejectsDuplicatePropertyNames()
    {
        using var result = await InvokeToolAsync(
            SkillTools.JsonSchemaGenerateToolName,
            new AIFunctionArguments
            {
                ["schemaName"] = "Duplicate",
                ["properties"] = new[]
                {
                    new SkillTools.JsonSchemaPropertyDefinition("p1", "name", "string", true),
                    new SkillTools.JsonSchemaPropertyDefinition("p2", "name", "number", false),
                },
            });

        Assert.That(result.RootElement.GetProperty("succeeded").GetBoolean(), Is.False);
        Assert.That(
            result.RootElement.GetProperty("errors")[0].GetString(),
            Is.EqualTo("Property name 'name' is duplicated."));
    }

    [Test]
    public async Task ValidateTool_AcceptsAndCanRevalidateTheSameSchema()
    {
        const string schema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object"}""";
        var arguments = new AIFunctionArguments { ["schemaJson"] = schema, ["sampleJson"] = "{}" };

        using var first = await InvokeToolAsync(SkillTools.JsonSchemaValidateToolName, arguments);
        using var second = await InvokeToolAsync(SkillTools.JsonSchemaValidateToolName, arguments);

        Assert.That(second.RootElement.GetProperty("instanceValid").GetBoolean(), Is.True);
        Assert.That(second.RootElement.GetProperty("report").GetString(), Does.Contain("Validation successful"));
    }

    [Test]
    public async Task ValidateTool_ReportsSchemaValidationErrors()
    {
        const string schema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "price": { "type": "number" } },
              "required": ["price"]
            }
            """;

        using var result = await InvokeToolAsync(
            SkillTools.JsonSchemaValidateToolName,
            new AIFunctionArguments { ["schemaJson"] = schema, ["sampleJson"] = """{"price":"wrong"}""" });

        Assert.That(result.RootElement.GetProperty("instanceValid").GetBoolean(), Is.False);
        Assert.That(
            result.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
                error.GetProperty("keyword").GetString() == "type"
                && error.GetProperty("path").GetString() == "/price"),
            Is.True);
    }

    [Test]
    public async Task ValidateTool_DistinguishesInvalidJsonAndInvalidSchema()
    {
        using var invalidJson = await InvokeToolAsync(
            SkillTools.JsonSchemaValidateToolName,
            new AIFunctionArguments { ["schemaJson"] = """{"type":"object"}""", ["sampleJson"] = """{"name":""" });
        using var invalidSchema = await InvokeToolAsync(
            SkillTools.JsonSchemaValidateToolName,
            new AIFunctionArguments { ["schemaJson"] = """{"type":"not-a-json-type"}""", ["sampleJson"] = "{}" });

        Assert.That(invalidJson.RootElement.GetProperty("status").GetString(), Is.EqualTo("Invalid JSON syntax"));
        Assert.That(invalidSchema.RootElement.GetProperty("status").GetString(), Is.EqualTo("Invalid JSON Schema"));
    }
}
