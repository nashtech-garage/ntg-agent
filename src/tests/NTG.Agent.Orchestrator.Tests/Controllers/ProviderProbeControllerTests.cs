using Microsoft.AspNetCore.Mvc;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class ProviderProbeControllerTests
{
    private Mock<IProviderModelService> _mockService = null!;
    private ProviderProbeController _controller = null!;

    [SetUp]
    public void Setup()
    {
        _mockService = new Mock<IProviderModelService>();
        // By default, treat the known provider names as known.
        _mockService.Setup(s => s.IsKnownProvider(It.IsAny<string>()))
            .Returns((string p) => p is "OpenAI" or "GitHubModel" or "GoogleGemini" or "Anthropic" or "AzureOpenAI");
        _controller = new ProviderProbeController(_mockService.Object);
    }

    [Test]
    public void Constructor_WhenServiceIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderProbeController(null!));
    }

    [Test]
    public async Task TestConnection_KnownProvider_ReturnsOkWithResult()
    {
        // Arrange
        var expected = new ProviderTestResult(true, "Connection successful.");
        _mockService.Setup(s => s.TestConnectionAsync(It.IsAny<ProviderProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _controller.TestConnection(new ProviderProbeRequest("OpenAI", null, "key"), CancellationToken.None);

        // Assert
        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.EqualTo(expected));
    }

    [Test]
    public async Task TestConnection_UnknownProvider_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.TestConnection(new ProviderProbeRequest("Bogus", null, "key"), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _mockService.Verify(s => s.TestConnectionAsync(It.IsAny<ProviderProbeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetModels_KnownProvider_ReturnsOkWithModels()
    {
        // Arrange
        _mockService.Setup(s => s.FetchModelsAsync(It.IsAny<ProviderProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["gpt-4o", "o1-mini"]);

        // Act
        var result = await _controller.GetModels(new ProviderProbeRequest("OpenAI", null, "key"), CancellationToken.None);

        // Assert
        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var payload = ok!.Value as ProviderModelsResponse;
        Assert.That(payload!.Models, Is.EqualTo(new[] { "gpt-4o", "o1-mini" }));
    }

    [Test]
    public async Task GetModels_UnknownProvider_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetModels(new ProviderProbeRequest("Bogus", null, "key"), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _mockService.Verify(s => s.FetchModelsAsync(It.IsAny<ProviderProbeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetModels_WhenProbeFails_ReturnsBadRequestWithFriendlyMessage()
    {
        // Arrange
        _mockService.Setup(s => s.FetchModelsAsync(It.IsAny<ProviderProbeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderProbeException("Authentication failed — check the API key."));

        // Act
        var result = await _controller.GetModels(new ProviderProbeRequest("OpenAI", null, "bad"), CancellationToken.None);

        // Assert
        var bad = result.Result as BadRequestObjectResult;
        Assert.That(bad, Is.Not.Null);
        Assert.That(bad!.Value, Is.EqualTo("Authentication failed — check the API key."));
    }
}
