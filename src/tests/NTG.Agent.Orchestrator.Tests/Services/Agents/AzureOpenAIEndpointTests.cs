using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

[TestFixture]
public class AzureOpenAIEndpointTests
{
    [TestCase("https://rmit-capstone-2026-hcm--resource.openai.azure.com")]
    [TestCase("https://rmit-capstone-2026-hcm--resource.openai.azure.com/")]
    public void ToV1_BareOrigin_AppendsV1Suffix(string endpoint)
    {
        var result = AzureOpenAIEndpoint.ToV1(endpoint);

        Assert.That(result.ToString(), Is.EqualTo("https://rmit-capstone-2026-hcm--resource.openai.azure.com/openai/v1"));
    }

    [TestCase("https://account.services.ai.azure.com")]
    [TestCase("https://account.services.ai.azure.com/")]
    public void ToV1_FoundryAccountRoot_AppendsV1Suffix(string endpoint)
    {
        var result = AzureOpenAIEndpoint.ToV1(endpoint);

        Assert.That(result.ToString(), Is.EqualTo("https://account.services.ai.azure.com/openai/v1"));
    }

    [TestCase("https://my-res.openai.azure.com/openai/v1")]
    [TestCase("https://my-res.openai.azure.com/openai/v1/")]
    public void ToV1_AlreadyV1_Unchanged(string endpoint)
    {
        var result = AzureOpenAIEndpoint.ToV1(endpoint);

        Assert.That(result.ToString(), Is.EqualTo("https://my-res.openai.azure.com/openai/v1"));
    }

    [Test]
    public void ToV1_AlreadyV1_MixedCase_DetectedWithoutAppendingSuffix()
    {
        var result = AzureOpenAIEndpoint.ToV1("https://my-res.openai.azure.com/OpenAI/V1");

        // Suffix detection is case-insensitive; input casing is preserved.
        Assert.That(result.ToString(), Is.EqualTo("https://my-res.openai.azure.com/OpenAI/V1"));
        Assert.That(result.AbsoluteUri, Does.Not.Contain("openai/v1/OpenAI"));
    }

    [Test]
    public void ToV1_WhitespaceOnly_Throws()
    {
        Assert.Throws<ArgumentException>(() => AzureOpenAIEndpoint.ToV1("   "));
    }
}
