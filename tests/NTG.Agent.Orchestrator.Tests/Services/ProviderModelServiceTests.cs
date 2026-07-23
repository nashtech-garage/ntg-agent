using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class ProviderModelServiceTests
{
    // Captures the outbound request and returns a canned response so per-provider
    // request shaping and response parsing can be asserted without a network call.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static ProviderModelService BuildService(StubHandler handler) =>
        new(new HttpClient(handler), NullLogger<ProviderModelService>.Instance);

    [Test]
    public async Task FetchModelsAsync_OpenAi_ParsesSortsAndDedupesDataArray()
    {
        // Arrange
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "data": [ { "id": "gpt-4o" }, { "id": "gpt-3.5" }, { "id": "gpt-4o" } ] }"""));
        var service = BuildService(handler);

        // Act
        var models = await service.FetchModelsAsync(new ProviderProbeRequest("OpenAI", null, "key"));

        // Assert
        Assert.That(models, Is.EqualTo(new[] { "gpt-3.5", "gpt-4o" }));
        Assert.That(handler.LastRequest!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/models"));
        Assert.That(handler.LastRequest.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo("key"));
    }

    [Test]
    public async Task FetchModelsAsync_Anthropic_UsesApiKeyAndVersionHeaders()
    {
        // Arrange
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "data": [ { "id": "claude-sonnet-4-6" } ] }"""));
        var service = BuildService(handler);

        // Act
        var models = await service.FetchModelsAsync(new ProviderProbeRequest("Anthropic", null, "key"));

        // Assert
        Assert.That(models, Is.EqualTo(new[] { "claude-sonnet-4-6" }));
        Assert.That(handler.LastRequest!.RequestUri!.ToString(), Is.EqualTo("https://api.anthropic.com/v1/models"));
        Assert.That(handler.LastRequest.Headers.GetValues("x-api-key"), Does.Contain("key"));
        Assert.That(handler.LastRequest.Headers.GetValues("anthropic-version"), Does.Contain("2023-06-01"));
    }

    [Test]
    public async Task FetchModelsAsync_Azure_ListsDeploymentsWithApiKeyHeader()
    {
        // Arrange
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "data": [ { "id": "my-gpt4-deployment" } ] }"""));
        var service = BuildService(handler);

        // Act
        var models = await service.FetchModelsAsync(
            new ProviderProbeRequest("AzureOpenAI", "https://my-res.openai.azure.com/", "key"));

        // Assert
        Assert.That(models, Is.EqualTo(new[] { "my-gpt4-deployment" }));
        Assert.That(handler.LastRequest!.RequestUri!.ToString(),
            Is.EqualTo("https://my-res.openai.azure.com/openai/deployments?api-version=2024-08-01-preview"));
        Assert.That(handler.LastRequest.Headers.GetValues("api-key"), Does.Contain("key"));
    }

    [Test]
    public async Task FetchModelsAsync_GitHub_ParsesBareArrayFromCatalog()
    {
        // Arrange — the catalog returns a bare array, and we fall back to "name" when "id" is absent.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[ { "name": "gpt-4o" }, { "id": "o1-mini" } ]"""));
        var service = BuildService(handler);

        // Act
        var models = await service.FetchModelsAsync(new ProviderProbeRequest("GitHubModel", null, "key"));

        // Assert
        Assert.That(models, Is.EqualTo(new[] { "gpt-4o", "o1-mini" }));
        Assert.That(handler.LastRequest!.RequestUri!.ToString(), Is.EqualTo("https://models.github.ai/catalog/models"));
    }

    [Test]
    public void FetchModelsAsync_Unauthorized_ThrowsFriendlyAuthMessage()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "nope")));

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("OpenAI", null, "bad")));
        Assert.That(ex!.Message, Is.EqualTo("Authentication failed — check the API key."));
    }

    [Test]
    public void FetchModelsAsync_Unreachable_ThrowsFriendlyReachMessage()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => throw new HttpRequestException("boom")));

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("OpenAI", null, "key")));
        Assert.That(ex!.Message, Is.EqualTo("Could not reach the provider endpoint."));
    }

    [Test]
    public void FetchModelsAsync_NonJsonBody_ThrowsFriendlyMessage()
    {
        // Arrange — 200 OK but a non-JSON body (e.g. a proxy login page). Must surface as
        // ProviderProbeException, not a raw JsonException that becomes a 500.
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "<html>not json</html>")));

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("OpenAI", null, "key")));
        Assert.That(ex!.Message, Is.EqualTo("Provider returned an unexpected (non-JSON) response."));
    }

    [Test]
    public async Task TestConnectionAsync_NonJsonBody_ReturnsFailureWithFriendlyMessage()
    {
        // Arrange — same malformed-body case through the test-connection path: the friendly
        // exception must be converted into a failed ProviderTestResult, not propagate.
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "<html>not json</html>")));

        // Act
        var result = await service.TestConnectionAsync(new ProviderProbeRequest("OpenAI", null, "key"));

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Provider returned an unexpected (non-JSON) response."));
    }

    [Test]
    public void FetchModelsAsync_UnknownProvider_Throws()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        // Act + Assert
        Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("Bogus", null, "key")));
    }

    [Test]
    public void FetchModelsAsync_MissingApiKey_Throws()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("OpenAI", null, "")));
        Assert.That(ex!.Message, Is.EqualTo("An API key is required."));
    }

    [Test]
    public void FetchModelsAsync_AzureWithoutEndpoint_Throws()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProviderProbeException>(() =>
            service.FetchModelsAsync(new ProviderProbeRequest("AzureOpenAI", null, "key")));
        Assert.That(ex!.Message, Is.EqualTo("Azure OpenAI requires a provider endpoint."));
    }

    [Test]
    public async Task TestConnectionAsync_Success_ReturnsSuccessWithCount()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "data": [ { "id": "gpt-4o" } ] }""")));

        // Act
        var result = await service.TestConnectionAsync(new ProviderProbeRequest("OpenAI", null, "key"));

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("1 model"));
    }

    [Test]
    public async Task TestConnectionAsync_AuthFailure_ReturnsFailureWithFriendlyMessage()
    {
        // Arrange
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "nope")));

        // Act
        var result = await service.TestConnectionAsync(new ProviderProbeRequest("OpenAI", null, "bad"));

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Authentication failed — check the API key."));
    }

    [Test]
    public void IsKnownProvider_DistinguishesKnownFromUnknown()
    {
        var service = BuildService(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));
        Assert.That(service.IsKnownProvider("OpenAI"), Is.True);
        Assert.That(service.IsKnownProvider("Bogus"), Is.False);
    }
}
