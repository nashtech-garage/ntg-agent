using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Chats;
using System.Security.Claims;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class ConversationsControllerTests
{
    private AgentDbContext _context;
    private ConversationsController _controller;
    private Guid _testUserId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _testUserId = Guid.NewGuid();

        // Mock the user principal
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
                new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
        ], "mock"));

        _controller = new ConversationsController(_context)
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
    public async Task GetConversations_WhenUserHasConversations_ReturnsOkWithCorrectlyOrderedConversations()
    {
        // Arrange
        await SeedConversationsData();

        // Act
        var actionResult = await _controller.GetConversations();

        // Assert
        Assert.That(actionResult, Is.Not.Null);
        var conversations = actionResult.Value as List<ConversationListItem>;
        Assert.That(conversations, Is.Not.Null.And.Count.EqualTo(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(conversations[0].Name, Is.EqualTo("User Conversation 2 (Most Recent)"));
            Assert.That(conversations[1].Name, Is.EqualTo("User Conversation 1"));
            Assert.That(conversations[2].Name, Is.EqualTo("User Conversation 3"));
        }
    }

    [Test]
    public async Task GetConversations_WhenUserHasNoConversations_ReturnsOkWithEmptyList()
    {
        // Act
        var actionResult = await _controller.GetConversations();

        // Assert
        var conversations = actionResult.Value as List<ConversationListItem>;
        Assert.That(conversations, Is.Not.Null);
        Assert.That(conversations, Is.Empty, "The list of conversations should be empty.");
    }

    [Test]
    public async Task GetConversation_WhenConversationExists_ReturnsConversation()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var expectedConversation = new Conversation
        {
            Id = conversationId,
            Name = "Test Conversation",
            UserId = _testUserId,
            SessionId = currentSessionId,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        };
        await _context.Conversations.AddAsync(expectedConversation);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetConversation(conversationId, currentSessionId.ToString());

        // Assert
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.TypeOf<Conversation>());
        var actualConversation = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualConversation.Id, Is.EqualTo(expectedConversation.Id));
            Assert.That(actualConversation.Name, Is.EqualTo(expectedConversation.Name));
        }
    }

    [Test]
    public async Task GetConversation_WhenConversationDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversation(nonExistentId, currentSessionId);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task GetConversationMessage_WhenConversationHasMessages_ReturnsCorrectlyOrderedMessages()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversationMessage(conversationId, currentSessionId);

        // Assert
        Assert.That(result.Value, Is.Not.Null);
        var messages = result.Value;
        Assert.That(messages, Has.Count.EqualTo(2), "Should return two messages, excluding the summary.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages[0].Content, Is.EqualTo("Hello"));
            Assert.That(messages[0].Role, Is.EqualTo((int)ChatRole.User));
            Assert.That(messages[1].Content, Is.EqualTo("Hi there!"));
            Assert.That(messages[1].Role, Is.EqualTo((int)ChatRole.Assistant));
        }
    }

    [Test]
    public async Task GetConversationMessage_WhenConversationHasNoMessages_ReturnsEmptyList()
    {
        // Arrange
        var conversation = new Conversation { Id = Guid.NewGuid(), UserId = _testUserId, Name = "Empty Convo" };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversationMessage(conversation.Id, currentSessionId);

        // Assert
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task GetConversationMessage_WhenAccessingOtherUsersConversation_ReturnsUnAuthorized()
    {
        // Arrange
        var (_, otherUserConversationId) = await SeedMessagesData();
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversationMessage(otherUserConversationId, currentSessionId);

        // Assert
        Assert.That(result.Result, Is.TypeOf<UnauthorizedResult>());

    }

    [Test]
    public async Task GetConversation_WhenAccessingOtherUsersConversation_ReturnsNotFound()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUserConversation = new Conversation 
        { 
            Id = Guid.NewGuid(), 
            UserId = otherUserId, 
            Name = "Other User's Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        await _context.Conversations.AddAsync(otherUserConversation);
        await _context.SaveChangesAsync();
        
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversation(otherUserConversation.Id, currentSessionId);

        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }

    // Rename the existing test and fix its assertion to expect NotFound
    [Test]
    public async Task GetConversationMessage_AccessingOtherUsersConversation_ReturnsUnAuthorized()
    {
        // Arrange
        var (_, otherUserConversationId) = await SeedMessagesData();
        var currentSessionId = string.Empty;

        // Act
        var result = await _controller.GetConversationMessage(otherUserConversationId, currentSessionId);

        // Assert
        Assert.That(result.Result, Is.TypeOf<UnauthorizedResult>());
    }

    private async Task SeedConversationsData()
    {
        var otherUserId = Guid.NewGuid();

        var conversations = new List<Conversation>
            {
                // User's conversations with different update times
                new() { Id = Guid.NewGuid(), Name = "User Conversation 1", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-2), UpdatedAt = DateTime.UtcNow.AddHours(-2) },
                new() { Id = Guid.NewGuid(), Name = "User Conversation 2 (Most Recent)", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow.AddHours(-1) },
                new() { Id = Guid.NewGuid(), Name = "User Conversation 3", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-3), UpdatedAt = DateTime.UtcNow.AddHours(-3) },

                // Another user's conversation
                new() { Id = Guid.NewGuid(), Name = "Other User's Conversation", UserId = otherUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };

        await _context.Conversations.AddRangeAsync(conversations);
        await _context.SaveChangesAsync();
    }

    private async Task<(Guid userConversationId, Guid otherUserConversationId)> SeedMessagesData()
    {
        var otherUserId = Guid.NewGuid();

        var userConversation = new Conversation { Id = Guid.NewGuid(), UserId = _testUserId, Name = "User's Convo" };
        var otherUserConversation = new Conversation { Id = Guid.NewGuid(), UserId = otherUserId, Name = "Other's Convo" };

        await _context.Conversations.AddRangeAsync(userConversation, otherUserConversation);

        var messages = new List<Models.Chat.ChatMessage>
        {
            // User's conversation messages
            new() { Id = Guid.NewGuid(), ConversationId = userConversation.Id, Content = "Hello", Role = ChatRole.User, CreatedAt = DateTime.UtcNow.AddMinutes(-10), IsSummary = false },
            new() { Id = Guid.NewGuid(), ConversationId = userConversation.Id, Content = "Hi there!", Role = ChatRole.Assistant, CreatedAt = DateTime.UtcNow.AddMinutes(-9), IsSummary = false },
            new() { Id = Guid.NewGuid(), ConversationId = userConversation.Id, Content = "This is a summary.", Role = ChatRole.Assistant, CreatedAt = DateTime.UtcNow.AddMinutes(-8), IsSummary = true },

            // Other user's conversation message
            new() { Id = Guid.NewGuid(), ConversationId = otherUserConversation.Id, Content = "Secret message", Role = ChatRole.User, CreatedAt = DateTime.UtcNow.AddMinutes(-5), IsSummary = false }
        };

        await _context.ChatMessages.AddRangeAsync(messages);
        await _context.SaveChangesAsync();

        return (userConversation.Id, otherUserConversation.Id);
    }
}