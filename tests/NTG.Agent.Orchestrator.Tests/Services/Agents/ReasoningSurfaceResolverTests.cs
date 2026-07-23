using NTG.Agent.Orchestrator.Services.Agents.Clients;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

// Encodes the routing contract: providers without a /responses endpoint (GitHub Models, Gemini's
// OpenAI-compat layer) and DeepSeek models (which reject reasoning.effort on the Responses API)
// must use Chat Completions reasoning; everything else uses the Responses API.
[TestFixture]
public class ReasoningSurfaceResolverTests
{
    private static Orchestrator.Models.Agents.Agent MakeAgent(string providerName, string modelName) =>
        new() { ProviderName = providerName, ProviderModelName = modelName };

    [TestCase("OpenAI", "gpt-5.1")]
    [TestCase("AzureOpenAI", "gpt-5.1")]
    public void Resolve_ResponsesCapableProvider_UsesResponsesApi(string provider, string model)
    {
        Assert.That(ReasoningSurfaceResolver.Resolve(MakeAgent(provider, model)),
            Is.EqualTo(ReasoningSurface.ResponsesApi));
    }

    [TestCase("OpenAI", "deepseek-v4-pro")]
    [TestCase("AzureOpenAI", "DeepSeek-V4-Pro")]
    public void Resolve_DeepSeekModel_UsesChatCompletionsEffort_OnAnyProvider(string provider, string model)
    {
        Assert.That(ReasoningSurfaceResolver.Resolve(MakeAgent(provider, model)),
            Is.EqualTo(ReasoningSurface.ChatCompletionsEffort));
    }

    [TestCase("GitHubModel", "openai/o4-mini")]
    [TestCase("GoogleGemini", "gemini-2.5-flash")]
    public void Resolve_ProviderWithoutResponsesEndpoint_UsesChatCompletionsEffort(string provider, string model)
    {
        Assert.That(ReasoningSurfaceResolver.Resolve(MakeAgent(provider, model)),
            Is.EqualTo(ReasoningSurface.ChatCompletionsEffort));
    }
}
