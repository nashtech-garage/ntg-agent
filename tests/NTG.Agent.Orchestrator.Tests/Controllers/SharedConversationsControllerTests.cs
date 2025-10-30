using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Enums;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Common.Dtos.SharedConversations;
using System.Security.Claims;
namespace NTG.Agent.Orchestrator.Tests.Controllers;
[TestFixture]
public class SharedConversationsControllerTests
{
    private AgentDbContext _context;
    private SharedConversationsController _controller;
    private Guid _testUserId;
    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _testUserId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Role, "User")
        ], "mock"));
        _controller = new SharedConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }
    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
    [Test]
    public void Constructor_WhenAgentDbContextIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedConversationsController(null!));
    }
    [Test]
    public void Constructor_WhenValidParameters_CreatesInstance()
    {
        var controller = new SharedConversationsController(_context);
        Assert.That(controller, Is.Not.Null);
    }
    [Test]
    public void ShareConversation_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        var request = new ShareConversationRequest
        {
            ConversationId = Guid.NewGuid()
        };
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.ShareConversation(request));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task ShareConversation_WhenNoMessages_ReturnsBadRequest()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var request = new ShareConversationRequest
        {
            ConversationId = conversationId
        };
        // Act
        var result = await _controller.ShareConversation(request);
        // Assert
        var badRequestResult = result as ActionResult<string>;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = badRequestResult.Result as BadRequestObjectResult;
        Assert.That(badRequest?.Value, Is.EqualTo("Conversation has no messages."));
    }
    [Test]
    public async Task ShareConversation_WhenValidConversationWithMessages_CreatesSharedConversation()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = conversationId,
            Name = "Test Conversation",
            UserId = _testUserId
        };
        var chatMessage = new PChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Content = "Test message",
            Role = ChatRole.User,
            UserId = _testUserId,
            IsSummary = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        _context.ChatMessages.Add(chatMessage);
        await _context.SaveChangesAsync();
        var request = new ShareConversationRequest
        {
            ConversationId = conversationId,
            Name = "Shared Test Conversation"
        };
        // Act
        var result = await _controller.ShareConversation(request);
        // Assert
        var actionResult = result as ActionResult<string>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.TypeOf<Guid>());
        var sharedId = (Guid)okResult.Value;
        // Verify shared conversation was created
        var sharedConversation = await _context.SharedConversations.FindAsync(sharedId);
        Assert.That(sharedConversation, Is.Not.Null);
        Assert.That(sharedConversation.OriginalConversationId, Is.EqualTo(conversationId));
        Assert.That(sharedConversation.Name, Is.EqualTo("Shared Test Conversation"));
        Assert.That(sharedConversation.UserId, Is.EqualTo(_testUserId));
        Assert.That(sharedConversation.Type, Is.EqualTo(SharedType.Conversation));
    }
    [Test]
    public async Task ShareConversation_WhenSpecificChatId_SharesSingleMessage()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var chatMessage = new PChatMessage
        {
            Id = chatId,
            ConversationId = conversationId,
            Content = "Specific message",
            Role = ChatRole.User,
            UserId = _testUserId,
            IsSummary = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ChatMessages.Add(chatMessage);
        await _context.SaveChangesAsync();
        var request = new ShareConversationRequest
        {
            ConversationId = conversationId,
            ChatId = chatId,
            Name = "Shared Message"
        };
        // Act
        var result = await _controller.ShareConversation(request);
        // Assert
        var actionResult = result as ActionResult<string>;
        var okResult = actionResult?.Result as OkObjectResult;
        var sharedId = (Guid)okResult?.Value!;
        var sharedConversation = await _context.SharedConversations.FindAsync(sharedId);
        Assert.That(sharedConversation, Is.Not.Null);
        Assert.That(sharedConversation.Type, Is.EqualTo(SharedType.Message));
    }
    [Test]
    public async Task ShareConversation_WhenExpirationProvided_SetsExpiration()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var chatMessage = new PChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Content = "Test message",
            Role = ChatRole.User,
            UserId = _testUserId,
            IsSummary = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ChatMessages.Add(chatMessage);
        await _context.SaveChangesAsync();
        var expirationDate = DateTime.UtcNow.AddDays(7);
        var request = new ShareConversationRequest
        {
            ConversationId = conversationId,
            ExpiresAt = expirationDate
        };
        // Act
        var result = await _controller.ShareConversation(request);
        // Assert
        var actionResult = result as ActionResult<string>;
        var okResult = actionResult?.Result as OkObjectResult;
        var sharedId = (Guid)okResult?.Value!;
        var sharedConversation = await _context.SharedConversations.FindAsync(sharedId);
        Assert.That(sharedConversation, Is.Not.Null);
        Assert.That(sharedConversation.ExpiresAt, Is.EqualTo(expirationDate));
    }
    [Test]
    public async Task GetSharedConversation_WhenSharedConversationNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetSharedConversation(Guid.NewGuid());
        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task GetSharedConversation_WhenSharedConversationInactive_ReturnsForbidden()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            IsActive = false
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetSharedConversation(sharedConversation.Id);
        // Assert
        Assert.That(result.Result, Is.TypeOf<StatusCodeResult>());
        var statusResult = result.Result as StatusCodeResult;
        Assert.That(statusResult?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }
    [Test]
    public async Task GetSharedConversation_WhenSharedConversationExpired_ReturnsGone()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Expired
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetSharedConversation(sharedConversation.Id);
        // Assert
        Assert.That(result.Result, Is.TypeOf<StatusCodeResult>());
        var statusResult = result.Result as StatusCodeResult;
        Assert.That(statusResult?.StatusCode, Is.EqualTo(StatusCodes.Status410Gone));
    }
    [Test]
    public async Task GetSharedConversation_WhenValidShare_ReturnsMessages()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        var sharedMessage = new SharedChatMessage
        {
            Id = Guid.NewGuid(),
            SharedConversationId = sharedConversation.Id,
            Content = "Shared message",
            Role = ChatRole.User,
            CreatedAt = DateTime.UtcNow
        };
        _context.SharedConversations.Add(sharedConversation);
        _context.SharedChatMessages.Add(sharedMessage);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetSharedConversation(sharedConversation.Id);
        // Assert
        var actionResult = result as ActionResult<IEnumerable<SharedChatMessage>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        var messages = okResult?.Value as IEnumerable<SharedChatMessage>;
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count(), Is.EqualTo(1));
        Assert.That(messages.First().Content, Is.EqualTo("Shared message"));
    }
    [Test]
    public void GetMyShares_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.GetMyShares());
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task GetMyShares_WhenNoSharedConversations_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetMyShares();
        // Assert
        var actionResult = result as ActionResult<IEnumerable<SharedConversation>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        var shares = okResult?.Value as IEnumerable<SharedConversation>;
        Assert.That(shares, Is.Not.Null);
        Assert.That(shares, Is.Empty);
    }
    [Test]
    public async Task GetMyShares_WhenSharedConversationsExist_ReturnsUserShares()
    {
        // Arrange
        var userShare = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "User Share",
            CreatedAt = DateTime.UtcNow
        };
        var otherUserShare = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), // Different user
            Name = "Other User Share",
            CreatedAt = DateTime.UtcNow
        };
        _context.SharedConversations.AddRange(userShare, otherUserShare);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetMyShares();
        // Assert
        var actionResult = result as ActionResult<IEnumerable<SharedConversation>>;
        var okResult = actionResult?.Result as OkObjectResult;
        var shares = okResult?.Value as IEnumerable<SharedConversation>;
        Assert.That(shares, Is.Not.Null);
        Assert.That(shares.Count(), Is.EqualTo(1));
        Assert.That(shares.First().UserId, Is.EqualTo(_testUserId));
        Assert.That(shares.First().Name, Is.EqualTo("User Share"));
    }
    [Test]
    public void Unshare_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.Unshare(Guid.NewGuid(), false));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task Unshare_WhenSharedConversationNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.Unshare(Guid.NewGuid(), false);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task Unshare_WhenSharedConversationBelongsToAnotherUser_ReturnsNotFound()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), // Different user
            IsActive = true
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.Unshare(sharedConversation.Id, false);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task Unshare_WhenValidRequest_UpdatesActiveStatus()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            IsActive = true
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.Unshare(sharedConversation.Id, false);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedShare = await _context.SharedConversations.FindAsync(sharedConversation.Id);
        Assert.That(updatedShare, Is.Not.Null);
        Assert.That(updatedShare.IsActive, Is.False);
    }
    [Test]
    public void UpdateExpiration_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        var request = new UpdateExpirationRequest { ExpiresAt = DateTime.UtcNow.AddDays(7) };
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.UpdateExpiration(Guid.NewGuid(), request));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task UpdateExpiration_WhenSharedConversationNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateExpirationRequest { ExpiresAt = DateTime.UtcNow.AddDays(7) };
        // Act
        var result = await _controller.UpdateExpiration(Guid.NewGuid(), request);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task UpdateExpiration_WhenValidRequest_UpdatesExpiration()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        var newExpiration = DateTime.UtcNow.AddDays(14);
        var request = new UpdateExpirationRequest { ExpiresAt = newExpiration };
        // Act
        var result = await _controller.UpdateExpiration(sharedConversation.Id, request);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedShare = await _context.SharedConversations.FindAsync(sharedConversation.Id);
        Assert.That(updatedShare, Is.Not.Null);
        Assert.That(updatedShare.ExpiresAt, Is.EqualTo(newExpiration));
    }
    [Test]
    public void DeleteSharedConversation_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.DeleteSharedConversation(Guid.NewGuid()));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task DeleteSharedConversation_WhenSharedConversationNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.DeleteSharedConversation(Guid.NewGuid());
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task DeleteSharedConversation_WhenValidRequest_DeletesSharedConversation()
    {
        // Arrange
        var sharedConversation = new SharedConversation
        {
            Id = Guid.NewGuid(),
            OriginalConversationId = Guid.NewGuid(),
            UserId = _testUserId
        };
        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteSharedConversation(sharedConversation.Id);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var deletedShare = await _context.SharedConversations.FindAsync(sharedConversation.Id);
        Assert.That(deletedShare, Is.Null);
    }
}
