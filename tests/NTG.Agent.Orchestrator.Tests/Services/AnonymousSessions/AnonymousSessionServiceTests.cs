using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NUnit.Framework;

namespace NTG.Agent.Orchestrator.Tests.Services.AnonymousSessions;

[TestFixture]
public class AnonymousSessionServiceTests
{
    private AgentDbContext _context = null!;
    private Mock<IIpAddressService> _mockIpAddressService = null!;
    private Mock<ILogger<AnonymousSessionService>> _mockLogger = null!;
    private AnonymousUserSettings _settings = null!;
    private AnonymousSessionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);

        _mockIpAddressService = new Mock<IIpAddressService>();
        _mockLogger = new Mock<ILogger<AnonymousSessionService>>();
        
        _settings = new AnonymousUserSettings
        {
            MaxMessagesPerSession = 10,
            ResetPeriodHours = 24,
            SessionExpirationDays = 7,
            EnableIpTracking = true,
            MaxMessagesPerIpPerDay = 50,
            CleanupProbability = 0
        };

        var optionsMock = new Mock<IOptions<AnonymousUserSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        _service = new AnonymousSessionService(
            _context,
            optionsMock.Object,
            _mockIpAddressService.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task CheckRateLimitAsync_NewSession_ShouldAllowMessages()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var ipAddress = "192.168.1.1";
        _mockIpAddressService.Setup(x => x.IsIpAllowedAsync(ipAddress))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CheckRateLimitAsync(sessionId, ipAddress);

        // Assert
        Assert.That(result.CanSendMessage, Is.True);
        Assert.That(result.CurrentCount, Is.EqualTo(0));
        Assert.That(result.MaxMessages, Is.EqualTo(10));
        Assert.That(result.RemainingMessages, Is.EqualTo(10));
        Assert.That(result.IsReadOnlyMode, Is.False);
    }

    [Test]
    public async Task CheckRateLimitAsync_SessionAtLimit_ShouldBlockMessages()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var ipAddress = "192.168.1.1";
        
        // Create session at limit
        var session = new AnonymousSession
        {
            SessionId = sessionId,
            IpAddress = ipAddress,
            MessageCount = 10
        };
        _context.AnonymousSessions.Add(session);
        await _context.SaveChangesAsync();

        _mockIpAddressService.Setup(x => x.IsIpAllowedAsync(ipAddress))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CheckRateLimitAsync(sessionId, ipAddress);

        // Assert
        Assert.That(result.CanSendMessage, Is.False);
        Assert.That(result.CurrentCount, Is.EqualTo(10));
        Assert.That(result.RemainingMessages, Is.EqualTo(0));
        Assert.That(result.IsReadOnlyMode, Is.True); // Graceful degradation enabled
        Assert.That(result.BlockReason, Is.EqualTo("session_limit"));
    }

    [Test]
    public async Task CheckRateLimitAsync_BlockedSession_ShouldReturnBlocked()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var ipAddress = "192.168.1.1";
        
        var session = new AnonymousSession
        {
            SessionId = sessionId,
            IpAddress = ipAddress,
            MessageCount = 5,
            IsBlocked = true
        };
        _context.AnonymousSessions.Add(session);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.CheckRateLimitAsync(sessionId, ipAddress);

        // Assert
        Assert.That(result.CanSendMessage, Is.False);
        Assert.That(result.IsReadOnlyMode, Is.False);
        Assert.That(result.BlockReason, Is.EqualTo("blocked"));
    }

    [Test]
    public async Task CheckRateLimitAsync_IpBlocked_ShouldReturnIpLimit()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var ipAddress = "192.168.1.1";
        
        _mockIpAddressService.Setup(x => x.IsIpAllowedAsync(ipAddress))
            .ReturnsAsync(false);

        // Act
        var result = await _service.CheckRateLimitAsync(sessionId, ipAddress);

        // Assert
        Assert.That(result.CanSendMessage, Is.False);
        Assert.That(result.IsReadOnlyMode, Is.True);
        Assert.That(result.BlockReason, Is.EqualTo("ip_limit"));
    }

    [Test]
    public async Task CheckRateLimitAsync_AfterResetPeriod_ShouldResetCounter()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var ipAddress = "192.168.1.1";
        
        var session = new AnonymousSession
        {
            SessionId = sessionId,
            IpAddress = ipAddress,
            MessageCount = 8,
            ResetAt = DateTime.UtcNow.AddHours(-25) // More than 24 hours ago
        };
        _context.AnonymousSessions.Add(session);
        await _context.SaveChangesAsync();

        _mockIpAddressService.Setup(x => x.IsIpAllowedAsync(ipAddress))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CheckRateLimitAsync(sessionId, ipAddress);

        // Assert
        Assert.That(result.CanSendMessage, Is.True);
        Assert.That(result.CurrentCount, Is.EqualTo(0)); // Should be reset
        Assert.That(result.RemainingMessages, Is.EqualTo(10));
    }


    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
