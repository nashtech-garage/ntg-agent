using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

[TestFixture]
public class ThinkingCapableModelsTests
{
    [TestCase(ProviderType.OpenAI, "o3-mini", true)]
    [TestCase(ProviderType.OpenAI, "gpt-5.1", true)]
    [TestCase(ProviderType.OpenAI, "gpt-4o", false)]
    [TestCase(ProviderType.AzureOpenAI, "o1-preview", true)]
    [TestCase(ProviderType.Anthropic, "claude-sonnet-4-5", true)]
    [TestCase(ProviderType.Anthropic, "claude-3-5-haiku", false)]
    [TestCase(ProviderType.GoogleGemini, "gemini-2.5-pro", true)]
    [TestCase(ProviderType.GoogleGemini, "gemini-1.5-flash", false)]
    [TestCase(ProviderType.OpenAICompatible, "deepseek-r1", true)]
    [TestCase(ProviderType.OpenAICompatible, "llama-3.1-70b", false)]
    public void Supports_MatchesExpectedCapability(ProviderType providerType, string modelId, bool expected)
    {
        Assert.That(ThinkingCapableModels.Supports(providerType, modelId), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Supports_WithBlankModel_ReturnsFalse(string? modelId)
    {
        Assert.That(ThinkingCapableModels.Supports(ProviderType.OpenAI, modelId), Is.False);
    }

    [Test]
    public void Supports_IsCaseInsensitive()
    {
        Assert.That(ThinkingCapableModels.Supports(ProviderType.OpenAI, "O3-MINI"), Is.True);
    }
}
