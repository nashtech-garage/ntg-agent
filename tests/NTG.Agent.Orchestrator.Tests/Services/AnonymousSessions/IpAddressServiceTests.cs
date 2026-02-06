using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using System.Net;

namespace NTG.Agent.Orchestrator.Tests.Services.AnonymousSessions;

[TestFixture]
public class IpAddressServiceTests
{
    private AgentDbContext _context = null!;
    private AnonymousUserSettings _settings = null!;
    private IpAddressService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        
        _settings = new AnonymousUserSettings
        {
            MaxMessagesPerSession = 10,
            EnableIpTracking = true,
            MaxMessagesPerIpPerDay = 50
        };

        var optionsMock = new Mock<IOptions<AnonymousUserSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        _service = new IpAddressService(_context, optionsMock.Object);
    }

    [Test]
    public void GetClientIpAddress_XForwardedFor_ShouldReturnFirstIp()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "192.168.1.1, 10.0.0.1";

        // Act
        var result = _service.GetClientIpAddress(context);

        // Assert
        Assert.That(result, Is.EqualTo("192.168.1.1"));
    }

    [Test]
    public void GetClientIpAddress_XRealIp_ShouldReturnRealIp()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Real-IP"] = "192.168.1.100";

        // Act
        var result = _service.GetClientIpAddress(context);

        // Assert
        Assert.That(result, Is.EqualTo("192.168.1.100"));
    }

    [Test]
    public void GetClientIpAddress_RemoteIpAddress_ShouldReturnRemoteIp()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.200");

        // Act
        var result = _service.GetClientIpAddress(context);

        // Assert
        Assert.That(result, Is.EqualTo("192.168.1.200"));
    }

    [Test]
    public void GetClientIpAddress_NullContext_ShouldReturnNull()
    {
        // Act
        var result = _service.GetClientIpAddress(null!);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task IsIpAllowedAsync_BelowLimit_ShouldReturnTrue()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        
        // Add some sessions but below the limit
        for (int i = 0; i < 30; i++)
        {
            _context.AnonymousSessions.Add(new AnonymousSession
            {
                SessionId = Guid.NewGuid(),
                IpAddress = ipAddress,
                MessageCount = 1,
                LastMessageAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.IsIpAllowedAsync(ipAddress);

        // Assert
        Assert.That(result, Is.True); // 30 messages < 50 limit
    }

    [Test]
    public async Task IsIpAllowedAsync_AtLimit_ShouldReturnFalse()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        
        // Add sessions at the limit
        for (int i = 0; i < 5; i++)
        {
            _context.AnonymousSessions.Add(new AnonymousSession
            {
                SessionId = Guid.NewGuid(),
                IpAddress = ipAddress,
                MessageCount = 10,
                LastMessageAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.IsIpAllowedAsync(ipAddress);

        // Assert
        Assert.That(result, Is.False); // 50 messages = 50 limit
    }

    [Test]
    public async Task IsIpAllowedAsync_IpTrackingDisabled_ShouldReturnTrue()
    {
        // Arrange
        _settings.EnableIpTracking = false;
        var ipAddress = "192.168.1.1";

        // Act
        var result = await _service.IsIpAllowedAsync(ipAddress);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsIpAllowedAsync_NullIpAddress_ShouldReturnTrue()
    {
        // Act
        var result = await _service.IsIpAllowedAsync(null!);

        // Assert
        Assert.That(result, Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
