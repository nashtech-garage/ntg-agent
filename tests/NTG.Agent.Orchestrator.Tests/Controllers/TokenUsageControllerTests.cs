using Microsoft.AspNetCore.Mvc;
using Moq;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Services.TokenTracking;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class TokenUsageControllerTests
{
    private Mock<ITokenTrackingService> _mockTokenTrackingService;
    private TokenUsageController _controller;

    [SetUp]
    public void Setup()
    {
        _mockTokenTrackingService = new Mock<ITokenTrackingService>();
        _controller = new TokenUsageController(_mockTokenTrackingService.Object);
    }


    [Test]
    public async Task GetStats_WithNoParameters_ReturnsOkWithStats()
    {
        // Arrange
        var expectedStats = new TokenUsageStatsDto(
            TotalInputTokens: 1000,
            TotalOutputTokens: 500,
            TotalTokens: 1500,
            TotalCost: 0.05m,
            TotalCalls: 10,
            UniqueUsers: 5,
            UniqueAnonymousSessions: 2,
            TokensByModel: new Dictionary<string, long?> { { "gpt-4", 1500 } },
            TokensByOperation: new Dictionary<string, long?> { { "Chat", 1500 } },
            CostByProvider: new Dictionary<string, decimal> { { "OpenAI", 0.05m } }
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageStatsAsync(null, null, null, null))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStats();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetUsageStatsAsync(null, null, null, null), Times.Once);
    }

    [Test]
    public async Task GetStats_WithUserId_ReturnsFilteredStats()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expectedStats = new TokenUsageStatsDto(
            TotalInputTokens: 200,
            TotalOutputTokens: 100,
            TotalTokens: 300,
            TotalCost: 0.01m,
            TotalCalls: 2,
            UniqueUsers: 1,
            UniqueAnonymousSessions: 0,
            TokensByModel: new Dictionary<string, long?> { { "gpt-4", 300 } },
            TokensByOperation: new Dictionary<string, long?> { { "Chat", 300 } },
            CostByProvider: new Dictionary<string, decimal> { { "OpenAI", 0.01m } }
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageStatsAsync(userId, null, null, null))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStats(userId);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetUsageStatsAsync(userId, null, null, null), Times.Once);
    }

    [Test]
    public async Task GetStats_WithSessionId_ReturnsFilteredStats()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedStats = new TokenUsageStatsDto(
            TotalInputTokens: 150,
            TotalOutputTokens: 75,
            TotalTokens: 225,
            TotalCost: 0.008m,
            TotalCalls: 1,
            UniqueUsers: 0,
            UniqueAnonymousSessions: 1,
            TokensByModel: new Dictionary<string, long?> { { "gpt-4", 225 } },
            TokensByOperation: new Dictionary<string, long?> { { "Chat", 225 } },
            CostByProvider: new Dictionary<string, decimal> { { "OpenAI", 0.008m } }
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageStatsAsync(null, sessionId, null, null))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStats(sessionId: sessionId);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetUsageStatsAsync(null, sessionId, null, null), Times.Once);
    }

    [Test]
    public async Task GetStats_WithDateRange_ReturnsFilteredStats()
    {
        // Arrange
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var expectedStats = new TokenUsageStatsDto(
            TotalInputTokens: 5000,
            TotalOutputTokens: 2500,
            TotalTokens: 7500,
            TotalCost: 0.25m,
            TotalCalls: 50,
            UniqueUsers: 10,
            UniqueAnonymousSessions: 5,
            TokensByModel: new Dictionary<string, long?> { { "gpt-4", 7500 } },
            TokensByOperation: new Dictionary<string, long?> { { "Chat", 7500 } },
            CostByProvider: new Dictionary<string, decimal> { { "OpenAI", 0.25m } }
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageStatsAsync(null, null, from, to))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStats(from: from, to: to);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetUsageStatsAsync(null, null, from, to), Times.Once);
    }

    [Test]
    public async Task GetStats_WithAllParameters_ReturnsFilteredStats()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var expectedStats = new TokenUsageStatsDto(
            TotalInputTokens: 100,
            TotalOutputTokens: 50,
            TotalTokens: 150,
            TotalCost: 0.005m,
            TotalCalls: 1,
            UniqueUsers: 1,
            UniqueAnonymousSessions: 0,
            TokensByModel: new Dictionary<string, long?> { { "gpt-4", 150 } },
            TokensByOperation: new Dictionary<string, long?> { { "Chat", 150 } },
            CostByProvider: new Dictionary<string, decimal> { { "OpenAI", 0.005m } }
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageStatsAsync(userId, sessionId, from, to))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStats(userId, sessionId, from, to);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetUsageStatsAsync(userId, sessionId, from, to), Times.Once);
    }

    

    [Test]
    public async Task GetHistory_WithDefaultParameters_ReturnsOkWithPagedResult()
    {
        // Arrange
        var expectedResult = new PagedResult<TokenUsageDto>(
            Items: new List<TokenUsageDto>
            {
                new TokenUsageDto(
                    Id: Guid.NewGuid(),
                    UserId: Guid.NewGuid(),
                    SessionId: Guid.NewGuid(),
                    UserEmail: "test@example.com",
                    ConversationId: Guid.NewGuid(),
                    ConversationName: "N/A",
                    MessageId: Guid.NewGuid(),
                    AgentId: Guid.NewGuid(),
                    AgentName: "Test Agent",
                    ModelName: "gpt-4",
                    ProviderName: "OpenAI",
                    InputTokens: 100,
                    OutputTokens: 50,
                    TotalTokens: 150,
                    InputTokenCost: 0.003m,
                    OutputTokenCost: 0.002m,
                    TotalCost: 0.005m,
                    OperationType: "Chat",
                    ResponseTime: TimeSpan.FromSeconds(2.5),
                    CreatedAt: DateTime.UtcNow
                )
            },
            TotalCount: 1,
            Page: 1,
            PageSize: 50,
            TotalPages: 1
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageHistoryAsync(null, null, 1, 50))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GetHistory();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedResult));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(null, null, 1, 50), Times.Once);
    }

    [Test]
    public async Task GetHistory_WithCustomPagination_ReturnsOkWithPagedResult()
    {
        // Arrange
        var expectedResult = new PagedResult<TokenUsageDto>(
            Items: new List<TokenUsageDto>(),
            TotalCount: 100,
            Page: 2,
            PageSize: 25,
            TotalPages: 4
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageHistoryAsync(null, null, 2, 25))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GetHistory(page: 2, pageSize: 25);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedResult));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(null, null, 2, 25), Times.Once);
    }

    [Test]
    public async Task GetHistory_WithDateRange_ReturnsOkWithFilteredResult()
    {
        // Arrange
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var expectedResult = new PagedResult<TokenUsageDto>(
            Items: new List<TokenUsageDto>(),
            TotalCount: 50,
            Page: 1,
            PageSize: 50,
            TotalPages: 1
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageHistoryAsync(from, to, 1, 50))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GetHistory(from: from, to: to);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedResult));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(from, to, 1, 50), Times.Once);
    }

    [Test]
    public async Task GetHistory_WithInvalidPageNumber_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetHistory(page: 0);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("Page number must be greater than 0"));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistory_WithNegativePageNumber_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetHistory(page: -1);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("Page number must be greater than 0"));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistory_WithInvalidPageSizeTooSmall_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetHistory(pageSize: 0);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("Page size must be between 1 and 100"));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistory_WithInvalidPageSizeTooLarge_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetHistory(pageSize: 101);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("Page size must be between 1 and 100"));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistory_WithNegativePageSize_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetHistory(pageSize: -5);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("Page size must be between 1 and 100"));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistory_WithMaxPageSize_ReturnsOkWithResult()
    {
        // Arrange
        var expectedResult = new PagedResult<TokenUsageDto>(
            Items: new List<TokenUsageDto>(),
            TotalCount: 100,
            Page: 1,
            PageSize: 100,
            TotalPages: 1
        );

        _mockTokenTrackingService
            .Setup(s => s.GetUsageHistoryAsync( null, null, 1, 100))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _controller.GetHistory(pageSize: 100);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedResult));
        _mockTokenTrackingService.Verify(s => s.GetUsageHistoryAsync(null, null, 1, 100), Times.Once);
    }

  

    [Test]
    public async Task GetStatsByUser_WithDefaultParameters_ReturnsOkWithAllUsers()
    {
        // Arrange
        var expectedStats = new List<UserTokenStatsDto>
        {
            new UserTokenStatsDto(
                UserId: Guid.NewGuid(),
                SessionId: null,
                Email: "user1@example.com",
                IsAnonymous: false,
                TotalInputTokens: 1000,
                TotalOutputTokens: 500,
                TotalTokens: 1500,
                TotalCost: 0.05m,
                ConversationCount: 5,
                MessageCount: 20,
                FirstActivity: DateTime.UtcNow.AddDays(-30),
                LastActivity: DateTime.UtcNow
            ),
            new UserTokenStatsDto(
                UserId: Guid.NewGuid(),
                SessionId: null,
                Email: "user2@example.com",
                IsAnonymous: false,
                TotalInputTokens: 800,
                TotalOutputTokens: 400,
                TotalTokens: 1200,
                TotalCost: 0.04m,
                ConversationCount: 3,
                MessageCount: 15,
                FirstActivity: DateTime.UtcNow.AddDays(-15),
                LastActivity: DateTime.UtcNow
            )
        };

        _mockTokenTrackingService
            .Setup(s => s.GetStatsByUserAsync(null, null, 0))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatsByUser();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetStatsByUserAsync(null, null, 0), Times.Once);
    }

    [Test]
    public async Task GetStatsByUser_WithTopN_ReturnsOkWithTopUsers()
    {
        // Arrange
        var expectedStats = new List<UserTokenStatsDto>
        {
            new UserTokenStatsDto(
                UserId: Guid.NewGuid(),
                SessionId: null,
                Email: "topuser@example.com",
                IsAnonymous: false,
                TotalInputTokens: 5000,
                TotalOutputTokens: 2500,
                TotalTokens: 7500,
                TotalCost: 0.25m,
                ConversationCount: 10,
                MessageCount: 50,
                FirstActivity: DateTime.UtcNow.AddDays(-60),
                LastActivity: DateTime.UtcNow
            )
        };

        _mockTokenTrackingService
            .Setup(s => s.GetStatsByUserAsync(null, null, 5))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatsByUser(topN: 5);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetStatsByUserAsync(null, null, 5), Times.Once);
    }

    [Test]
    public async Task GetStatsByUser_WithDateRange_ReturnsOkWithFilteredStats()
    {
        // Arrange
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var expectedStats = new List<UserTokenStatsDto>();

        _mockTokenTrackingService
            .Setup(s => s.GetStatsByUserAsync(from, to, 0))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatsByUser(from: from, to: to);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetStatsByUserAsync(from, to, 0), Times.Once);
    }

    [Test]
    public async Task GetStatsByUser_WithAllParameters_ReturnsOkWithFilteredStats()
    {
        // Arrange
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var expectedStats = new List<UserTokenStatsDto>
        {
            new UserTokenStatsDto(
                UserId: Guid.NewGuid(),
                SessionId: null,
                Email: "user@example.com",
                IsAnonymous: false,
                TotalInputTokens: 2000,
                TotalOutputTokens: 1000,
                TotalTokens: 3000,
                TotalCost: 0.10m,
                ConversationCount: 8,
                MessageCount: 30,
                FirstActivity: from,
                LastActivity: to
            )
        };

        _mockTokenTrackingService
            .Setup(s => s.GetStatsByUserAsync(from, to, 10))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatsByUser(from: from, to: to, topN: 10);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(expectedStats));
        _mockTokenTrackingService.Verify(s => s.GetStatsByUserAsync(from, to, 10), Times.Once);
    }

    [Test]
    public async Task GetStatsByUser_WithNegativeTopN_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetStatsByUser(topN: -1);

        // Assert
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult!.Value, Is.EqualTo("TopN must be greater than or equal to 0"));
        _mockTokenTrackingService.Verify(s => s.GetStatsByUserAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetStatsByUser_WithAnonymousUser_ReturnsOkWithAnonymousStats()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedStats = new List<UserTokenStatsDto>
        {
            new UserTokenStatsDto(
                UserId: null,
                SessionId: sessionId,
                Email: $"Anonymous Session {sessionId}",
                IsAnonymous: true,
                TotalInputTokens: 300,
                TotalOutputTokens: 150,
                TotalTokens: 450,
                TotalCost: 0.015m,
                ConversationCount: 2,
                MessageCount: 8,
                FirstActivity: DateTime.UtcNow.AddHours(-2),
                LastActivity: DateTime.UtcNow
            )
        };

        _mockTokenTrackingService
            .Setup(s => s.GetStatsByUserAsync(null, null, 0))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatsByUser();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var stats = okResult!.Value as List<UserTokenStatsDto>;
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats![0].IsAnonymous, Is.True);
        Assert.That(stats[0].UserId, Is.Null);
        Assert.That(stats[0].SessionId, Is.EqualTo(sessionId));
    }
}
