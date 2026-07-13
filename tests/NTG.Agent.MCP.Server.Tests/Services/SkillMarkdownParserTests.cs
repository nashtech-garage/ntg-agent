using NTG.Agent.MCP.Server.Services;

namespace NTG.Agent.MCP.Server.Tests.Services;

[TestFixture]
public class SkillMarkdownParserTests
{
    [Test]
    public void TryParse_WithValidFrontmatter_ReturnsNameAndDescription()
    {
        var content = """
            ---
            name: expense-report
            description: Helps with expense reports.
            ---

            # Instructions
            """;

        var result = SkillMarkdownParser.TryParse(content, out var frontmatter, out var error);

        Assert.That(result, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(frontmatter!.Name, Is.EqualTo("expense-report"));
        Assert.That(frontmatter.Description, Is.EqualTo("Helps with expense reports."));
    }

    [Test]
    public void TryParse_WithQuotedValuesAndExtraFields_Succeeds()
    {
        var content = "---\nname: \"unit-converter\"\nlicense: MIT\ndescription: 'Converts units: metric and imperial.'\n---\nBody";

        var result = SkillMarkdownParser.TryParse(content, out var frontmatter, out _);

        Assert.That(result, Is.True);
        Assert.That(frontmatter!.Name, Is.EqualTo("unit-converter"));
        Assert.That(frontmatter.Description, Is.EqualTo("Converts units: metric and imperial."));
    }

    [Test]
    public void TryParse_WithWindowsLineEndings_Succeeds()
    {
        var content = "---\r\nname: my-skill\r\ndescription: Something useful.\r\n---\r\nBody";

        var result = SkillMarkdownParser.TryParse(content, out var frontmatter, out _);

        Assert.That(result, Is.True);
        Assert.That(frontmatter!.Name, Is.EqualTo("my-skill"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TryParse_WithEmptyContent_Fails(string? content)
    {
        var result = SkillMarkdownParser.TryParse(content, out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("empty"));
    }

    [Test]
    public void TryParse_WithoutFrontmatterBlock_Fails()
    {
        var result = SkillMarkdownParser.TryParse("# Just markdown, no frontmatter", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("frontmatter"));
    }

    [Test]
    public void TryParse_WithUnclosedFrontmatter_Fails()
    {
        var result = SkillMarkdownParser.TryParse("---\nname: x-skill\ndescription: y\n# never closed", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("not closed"));
    }

    [Test]
    public void TryParse_WithMissingName_Fails()
    {
        var result = SkillMarkdownParser.TryParse("---\ndescription: y\n---\nBody", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("'name'"));
    }

    [Test]
    public void TryParse_WithMissingDescription_Fails()
    {
        var result = SkillMarkdownParser.TryParse("---\nname: my-skill\n---\nBody", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("'description'"));
    }

    [TestCase("My-Skill")]
    [TestCase("my skill")]
    [TestCase("my--skill")]
    [TestCase("-myskill")]
    [TestCase("myskill-")]
    [TestCase("skill.name")]
    public void TryParse_WithInvalidName_Fails(string name)
    {
        var result = SkillMarkdownParser.TryParse($"---\nname: {name}\ndescription: y\n---\nBody", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("invalid"));
    }

    [Test]
    public void TryParse_WithTooLongDescription_Fails()
    {
        var description = new string('a', SkillMarkdownParser.MaxDescriptionLength + 1);

        var result = SkillMarkdownParser.TryParse($"---\nname: my-skill\ndescription: {description}\n---\nBody", out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("description"));
    }

    [Test]
    public void TryParse_WithTooLargeContent_Fails()
    {
        var content = "---\nname: my-skill\ndescription: y\n---\n" + new string('a', SkillMarkdownParser.MaxContentLength);

        var result = SkillMarkdownParser.TryParse(content, out _, out var error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("maximum size"));
    }
}
