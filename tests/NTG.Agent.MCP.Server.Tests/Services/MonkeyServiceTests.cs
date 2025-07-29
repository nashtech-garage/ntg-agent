using Moq;
using Moq.Protected;
using NTG.Agent.MCP.Server.Services;
using System.Net;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.Tests.Services;

[TestFixture]
public class MonkeyServiceTests
{
    private Mock<IHttpClientFactory> _clientFactoryMock;

    private readonly List<Monkey> _monkeys = [
            new Monkey()
            {
                Image = "Monkey Image",
                Name = "Monkey Name",
                Location = "Monkey Location",
                Latitude = 1,
                Longitude = 1,
                Population = 500,
                Details = "Monkey Details"
            }
        ];

    [SetUp]
    public void Setup()
    {
        _clientFactoryMock = new Mock<IHttpClientFactory>();
    }

    [Test]
    public async Task GetMonkeys_WhenResponseIsNotSuccessful_ReturnsEmptyList()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(JsonSerializer.Serialize(new List<Monkey>()))
            });

        var httpClient = new HttpClient(handlerMock.Object);
        _clientFactoryMock.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var sut = new MonkeyService(_clientFactoryMock.Object);

        // Act
        var actual = await sut.GetMonkeys();

        // Assert
        Assert.That(actual, Is.Not.Null.And.Empty);
    }

    [Test]
    public async Task GetMonkeys_WhenResponseIsSuccessful_ReturnsListOfMonkeys()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(_monkeys))
            });

        var httpClient = new HttpClient(handlerMock.Object);
        _clientFactoryMock.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var sut = new MonkeyService(_clientFactoryMock.Object);

        // Act
        var actual = await sut.GetMonkeys();

        // Assert
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Has.Count.EqualTo(1));

        Assert.Multiple(() =>
        {
            var monkey = actual[0];

            Assert.That(monkey.Name, Is.EqualTo("Monkey Name"));
            Assert.That(monkey.Image, Is.EqualTo("Monkey Image"));
            Assert.That(monkey.Location, Is.EqualTo("Monkey Location"));
            Assert.That(monkey.Latitude, Is.EqualTo(1));
            Assert.That(monkey.Population, Is.EqualTo(500));
            Assert.That(monkey.Details, Is.EqualTo("Monkey Details"));
        });
    }

    [Test]
    public async Task GetMonkey_WhenMonkeyExists_ReturnsMonkey()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(_monkeys))
            });

        var httpClient = new HttpClient(handlerMock.Object);
        _clientFactoryMock.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var sut = new MonkeyService(_clientFactoryMock.Object);

        // Act
        var actual = await sut.GetMonkey("Monkey Name");

        // Assert
        Assert.That(actual, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Name, Is.EqualTo("Monkey Name"));
            Assert.That(actual.Image, Is.EqualTo("Monkey Image"));
            Assert.That(actual.Location, Is.EqualTo("Monkey Location"));
            Assert.That(actual.Latitude, Is.EqualTo(1));
            Assert.That(actual.Population, Is.EqualTo(500));
            Assert.That(actual.Details, Is.EqualTo("Monkey Details"));
        });
    }

    [Test]
    public async Task GetMonkey_WhenMonkeyDoesNotExist_ReturnsNull()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(_monkeys))
            });

        var httpClient = new HttpClient(handlerMock.Object);
        _clientFactoryMock.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var sut = new MonkeyService(_clientFactoryMock.Object);

        // Act
        var actual = await sut.GetMonkey("Monkey Name 2");

        // Assert
        Assert.That(actual, Is.Null);
    }
}