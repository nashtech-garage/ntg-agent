using NTG.Agent.LightRag;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class LightRagSettingsTests
{
    [Test]
    public void ResolveWebUiUrl_WhenNoWebUiGatewayIsConfigured_UsesLocalGatewayFallback()
    {
        var agentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var settings = new LightRagSettings();

        var result = settings.ResolveWebUiUrl(agentId);

        Assert.That(result, Is.EqualTo("http://agent-11111111-2222-3333-4444-555555555555.localhost:8080/webui/"));
    }

    [Test]
    public void ResolveWebUiUrl_WhenRemoteWebUiGatewayIsConfigured_UsesAgentSubdomain()
    {
        var agentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var settings = new LightRagSettings
        {
            WebUiGatewayUrl = "https://lightrag.example.com"
        };

        var result = settings.ResolveWebUiUrl(agentId);

        Assert.That(result, Is.EqualTo("https://agent-11111111-2222-3333-4444-555555555555.lightrag.example.com/webui/"));
    }

    [Test]
    public void ResolveWebUiUrl_WhenGatewayUrlIsInvalid_ThrowsInvalidOperationException()
    {
        var settings = new LightRagSettings
        {
            WebUiGatewayUrl = "not-a-url"
        };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveWebUiUrl(Guid.NewGuid()));
    }
}
