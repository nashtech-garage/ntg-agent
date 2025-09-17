using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Conversations;
using NTG.Agent.Shared.Dtos.Enums;
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

    [Test]
    public async Task UpdateMessageReaction_WhenMessageExists_ReturnsNoContent()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        var request = new UpdateReactionRequest { Reaction = ReactionType.Like };
        // Act
        var result = await _controller.UpdateMessageReaction(conversationId, messageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.Reaction, Is.EqualTo(ReactionType.Like));
    }
  
    [Test]
    public async Task UpdateMessageReaction_WhenMessageDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var nonExistentMessageId = Guid.NewGuid();
        var request = new UpdateReactionRequest { Reaction = ReactionType.Like };
        // Act
        var result = await _controller.UpdateMessageReaction(conversationId, nonExistentMessageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
  
    [Test]
    public async Task UpdateMessageReaction_WhenAccessingOtherUsersMessage_ReturnsUnauthorized()
    {
        // Arrange
        var (_, otherUserConversationId) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == otherUserConversationId)
            .First().Id;
        var request = new UpdateReactionRequest { Reaction = ReactionType.Like };
        // Act
        var result = await _controller.UpdateMessageReaction(otherUserConversationId, messageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }
  
    [Test]
    public async Task UpdateMessageReaction_WhenChangingReaction_UpdatesCorrectly()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        // First set to Like
        var likeRequest = new UpdateReactionRequest { Reaction = ReactionType.Like };
        await _controller.UpdateMessageReaction(conversationId, messageId, likeRequest);
        // Then change to Dislike
        var dislikeRequest = new UpdateReactionRequest { Reaction = ReactionType.Dislike };
        // Act
        var result = await _controller.UpdateMessageReaction(conversationId, messageId, dislikeRequest);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.Reaction, Is.EqualTo(ReactionType.Dislike));
    }
  
    [Test]
    public async Task UpdateMessageReaction_WhenSettingToNone_ClearsReaction()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        // First set to Like
        var likeRequest = new UpdateReactionRequest { Reaction = ReactionType.Like };
        await _controller.UpdateMessageReaction(conversationId, messageId, likeRequest);
        // Then clear reaction
        var noneRequest = new UpdateReactionRequest { Reaction = ReactionType.None };
        // Act
        var result = await _controller.UpdateMessageReaction(conversationId, messageId, noneRequest);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.Reaction, Is.EqualTo(ReactionType.None));
    }
  
    [Test]
    public async Task UpdateMessageComment_WhenMessageExists_ReturnsNoContent()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        var request = new UpdateCommentRequest { Comment = "This is a test comment" };
        // Act
        var result = await _controller.UpdateMessageComment(conversationId, messageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.UserComment, Is.EqualTo("This is a test comment"));
    }
  
    [Test]
    public async Task UpdateMessageComment_WhenMessageDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var nonExistentMessageId = Guid.NewGuid();
        var request = new UpdateCommentRequest { Comment = "Test comment" };
        // Act
        var result = await _controller.UpdateMessageComment(conversationId, nonExistentMessageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
  
    [Test]
    public async Task UpdateMessageComment_WhenAccessingOtherUsersMessage_ReturnsUnauthorized()
    {
        // Arrange
        var (_, otherUserConversationId) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == otherUserConversationId)
            .First().Id;
        var request = new UpdateCommentRequest { Comment = "Test comment" };
        // Act
        var result = await _controller.UpdateMessageComment(otherUserConversationId, messageId, request);
        // Assert
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }
  
    [Test]
    public async Task UpdateMessageComment_WhenUpdatingExistingComment_ReplacesContent()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        // First set a comment
        var firstRequest = new UpdateCommentRequest { Comment = "First comment" };
        await _controller.UpdateMessageComment(conversationId, messageId, firstRequest);
        // Then update the comment
        var secondRequest = new UpdateCommentRequest { Comment = "Updated comment" };
        // Act
        var result = await _controller.UpdateMessageComment(conversationId, messageId, secondRequest);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.UserComment, Is.EqualTo("Updated comment"));
    }
  
    [Test]
    public async Task UpdateMessageComment_WhenClearingComment_SetsEmptyString()
    {
        // Arrange
        var (conversationId, _) = await SeedMessagesData();
        var messageId = _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.Role == ChatRole.Assistant)
            .First().Id;
        // First set a comment
        var firstRequest = new UpdateCommentRequest { Comment = "Initial comment" };
        await _controller.UpdateMessageComment(conversationId, messageId, firstRequest);
        // Then clear the comment
        var clearRequest = new UpdateCommentRequest { Comment = string.Empty };
        // Act
        var result = await _controller.UpdateMessageComment(conversationId, messageId, clearRequest);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedMessage = await _context.ChatMessages.FindAsync(messageId);
        Assert.That(updatedMessage, Is.Not.Null);
        Assert.That(updatedMessage.UserComment, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task SearchConversationMessages_WhenKeywordMatches_ReturnsMatchingResults()
    {
        // Arrange
        await SeedSearchData();
        var keyword = "Hello";
        // Act
        var actionResult = await _controller.SearchConversationMessages(keyword);
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Has.Count.GreaterThan(0));
        var messageResult = searchResults.First(r => !r.IsConversation);
        Assert.That(messageResult.Content, Contains.Substring("Hello"));
        Assert.That(messageResult.Role, Is.EqualTo((int)ChatRole.User));
    }
  
    [Test]
    public async Task SearchConversationMessages_WhenKeywordInConversationName_ReturnsConversationResults()
    {
        // Arrange
        await SeedSearchData();
        var keyword = "Important";
        // Act
        var actionResult = await _controller.SearchConversationMessages(keyword);
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Has.Count.GreaterThan(0));
        var conversationResult = searchResults.First(r => r.IsConversation);
        Assert.That(conversationResult.Content, Contains.Substring("Important"));
        Assert.That(conversationResult.Role, Is.EqualTo((int)ChatRole.User));
        Assert.That(conversationResult.IsConversation, Is.True);
    }
    [Test]
    public async Task SearchConversationMessages_WhenEmptyKeyword_ReturnsEmptyList()
    {
        // Arrange
        await SeedSearchData();
        // Act
        var actionResult = await _controller.SearchConversationMessages("");
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Is.Empty);
    }
    [Test]
    public async Task SearchConversationMessages_WhenWhitespaceKeyword_ReturnsEmptyList()
    {
        // Arrange
        await SeedSearchData();
        // Act
        var actionResult = await _controller.SearchConversationMessages("   ");
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Is.Empty);
    }
    [Test]
    public async Task SearchConversationMessages_WhenNoMatches_ReturnsEmptyList()
    {
        // Arrange
        await SeedSearchData();
        var keyword = "NonExistentKeyword123";
        // Act
        var actionResult = await _controller.SearchConversationMessages(keyword);
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Is.Empty);
    }
    [Test]
    public async Task SearchConversationMessages_WhenLongAssistantMessage_ReturnsContextualContent()
    {
        // Arrange
        var longContent = new string('a', 500) + "SearchKeyword" + new string('b', 500);
        var conversation = new Conversation { Id = Guid.NewGuid(), UserId = _testUserId, Name = "Test Convo" };
        await _context.Conversations.AddAsync(conversation);
        var message = new Models.Chat.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Content = longContent,
            Role = ChatRole.Assistant,
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            IsSummary = false
        };
        await _context.ChatMessages.AddAsync(message);
        await _context.SaveChangesAsync();
        // Act
        var actionResult = await _controller.SearchConversationMessages("SearchKeyword");
        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var searchResults = okResult.Value as IList<ChatSearchResultItem>;
        Assert.That(searchResults, Is.Not.Null);
        Assert.That(searchResults, Has.Count.EqualTo(1));
        var messageResult = searchResults.First();
        Assert.That(messageResult.Content, Contains.Substring("SearchKeyword"));
        Assert.That(messageResult.Content.Length, Is.LessThan(longContent.Length), "Content should be truncated");
        Assert.That(messageResult.Content, Contains.Substring("..."), "Should contain ellipses indicating truncation");
    }
    [Test]
    public async Task PutConversation_WhenIdsMatch_ReturnsNoContent()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        conversation.Name = "Updated Name";
        // Act
        var result = await _controller.PutConversation(conversation.Id, conversation);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedConversation = await _context.Conversations.FindAsync(conversation.Id);
        Assert.That(updatedConversation, Is.Not.Null);
        Assert.That(updatedConversation.Name, Is.EqualTo("Updated Name"));
    }
    [Test]
    public async Task PutConversation_WhenIdsMismatch_ReturnsBadRequest()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Test Conversation",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var differentId = Guid.NewGuid();
        // Act
        var result = await _controller.PutConversation(differentId, conversation);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestResult>());
    }
    [Test]
    public async Task PutConversation_WhenConversationDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var nonExistentConversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Non-existent Conversation",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        // Act
        var result = await _controller.PutConversation(nonExistentConversation.Id, nonExistentConversation);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task RenameConversation_WhenConversationExists_ReturnsNoContent()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        var originalUpdatedAt = conversation.UpdatedAt;
        var newName = "New Conversation Name";
        // Add a small delay to ensure UpdatedAt will be different
        await Task.Delay(10);
        // Act
        var result = await _controller.RenameConversation(conversation.Id, newName);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedConversation = await _context.Conversations.FindAsync(conversation.Id);
        Assert.That(updatedConversation, Is.Not.Null);
        Assert.That(updatedConversation.Name, Is.EqualTo(newName));
        Assert.That(updatedConversation.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }
    [Test]
    public async Task RenameConversation_WhenConversationDoesNotExist_ReturnsBadRequest()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var newName = "New Name";
        // Act
        var result = await _controller.RenameConversation(nonExistentId, newName);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestResult>());
    }
    [Test]
    public async Task RenameConversation_WhenAccessingOtherUsersConversation_ReturnsBadRequest()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUserConversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Other User's Conversation",
            UserId = otherUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(otherUserConversation);
        await _context.SaveChangesAsync();
        var newName = "Hacked Name";
        // Act
        var result = await _controller.RenameConversation(otherUserConversation.Id, newName);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestResult>());
    }
    [Test]
    public async Task PostConversation_WhenUserIsAuthenticated_CreatesConversationWithUserId()
    {
        // Act
        var result = await _controller.PostConversation(null);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        Assert.That(createdResult.Value, Is.TypeOf<ConversationCreated>());
        var conversationCreated = createdResult.Value as ConversationCreated;
        Assert.That(conversationCreated, Is.Not.Null);
        Assert.That(conversationCreated.Name, Is.EqualTo("New Conversation"));
        var conversation = await _context.Conversations.FindAsync(conversationCreated.Id);
        Assert.That(conversation, Is.Not.Null);
        Assert.That(conversation.UserId, Is.EqualTo(_testUserId));
        Assert.That(conversation.SessionId, Is.Null);
    }
    [Test]
    public async Task PostConversation_WhenUserIsAnonymous_CreatesConversationWithSessionId()
    {
        // Arrange
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        var sessionId = Guid.NewGuid().ToString();
        // Act
        var result = await anonymousController.PostConversation(sessionId);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var conversationCreated = createdResult.Value as ConversationCreated;
        Assert.That(conversationCreated, Is.Not.Null);
        var conversation = await _context.Conversations.FindAsync(conversationCreated.Id);
        Assert.That(conversation, Is.Not.Null);
        Assert.That(conversation.UserId, Is.Null);
        Assert.That(conversation.SessionId, Is.EqualTo(Guid.Parse(sessionId)));
    }
    [Test]
    public async Task PostConversation_WhenAnonymousWithoutSessionId_CreatesConversationWithoutSessionId()
    {
        // Arrange
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.PostConversation(null);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var conversationCreated = createdResult.Value as ConversationCreated;
        Assert.That(conversationCreated, Is.Not.Null);
        var conversation = await _context.Conversations.FindAsync(conversationCreated.Id);
        Assert.That(conversation, Is.Not.Null);
        Assert.That(conversation.UserId, Is.Null);
        Assert.That(conversation.SessionId, Is.Null);
    }
    [Test]
    public async Task DeleteConversation_WhenConversationExists_ReturnsNoContent()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "To Be Deleted",
            UserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteConversation(conversation.Id);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var deletedConversation = await _context.Conversations.FindAsync(conversation.Id);
        Assert.That(deletedConversation, Is.Null);
    }
    [Test]
    public async Task DeleteConversation_WhenConversationDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        // Act
        var result = await _controller.DeleteConversation(nonExistentId);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task DeleteConversation_WhenAccessingOtherUsersConversation_ReturnsNotFound()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUserConversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Other User's Conversation",
            UserId = otherUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(otherUserConversation);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteConversation(otherUserConversation.Id);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
        // Verify conversation still exists
        var stillExists = await _context.Conversations.FindAsync(otherUserConversation.Id);
        Assert.That(stillExists, Is.Not.Null);
    }
    [Test]
    public async Task GetConversation_WhenAnonymousUserWithValidSessionId_ReturnsConversation()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Anonymous Conversation",
            UserId = null,
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversation(conversation.Id, sessionId.ToString());
        // Assert
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Id, Is.EqualTo(conversation.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Anonymous Conversation"));
    }
    [Test]
    public async Task GetConversation_WhenAnonymousUserWithInvalidSessionId_ReturnsBadRequest()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var invalidSessionId = "invalid-session-id";
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversation(conversationId, invalidSessionId);
        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("A valid Session ID is required for unauthenticated requests."));
    }
    [Test]
    public async Task GetConversation_WhenAnonymousUserWithWrongSessionId_ReturnsNotFound()
    {
        // Arrange
        var correctSessionId = Guid.NewGuid();
        var wrongSessionId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Anonymous Conversation",
            UserId = null,
            SessionId = correctSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversation(conversation.Id, wrongSessionId.ToString());
        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task GetConversationMessage_WhenAnonymousUserWithValidSessionId_ReturnsMessages()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Anonymous Conversation",
            UserId = null,
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        var message = new Models.Chat.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Content = "Anonymous message",
            Role = ChatRole.User,
            CreatedAt = DateTime.UtcNow,
            IsSummary = false
        };
        await _context.ChatMessages.AddAsync(message);
        await _context.SaveChangesAsync();
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversationMessage(conversation.Id, sessionId.ToString());
        // Assert
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value[0].Content, Is.EqualTo("Anonymous message"));
    }
    [Test]
    public async Task GetConversationMessage_WhenAnonymousUserWithInvalidSessionId_ReturnsBadRequest()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var invalidSessionId = "invalid-session-id";
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversationMessage(conversationId, invalidSessionId);
        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("A valid Session ID is required for unauthenticated requests."));
    }
    [Test]
    public async Task GetConversationMessage_WhenAnonymousUserWithWrongSessionId_ReturnsUnauthorized()
    {
        // Arrange
        var correctSessionId = Guid.NewGuid();
        var wrongSessionId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Name = "Anonymous Conversation",
            UserId = null,
            SessionId = correctSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversationMessage(conversation.Id, wrongSessionId.ToString());
        // Assert
        Assert.That(result.Result, Is.TypeOf<UnauthorizedResult>());
    }
    [Test]
    public async Task GetConversationMessage_WhenAnonymousUserWithoutSessionId_ReturnsUnauthorized()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var anonymousController = new ConversationsController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        // Act
        var result = await anonymousController.GetConversationMessage(conversationId, null);
        // Assert
        Assert.That(result.Result, Is.TypeOf<UnauthorizedResult>());
    }
    private async Task SeedSearchData()
    {
        var otherUserId = Guid.NewGuid();
        var conversations = new List<Conversation>
        {
            new() { Id = Guid.NewGuid(), Name = "Important Meeting Notes", UserId = _testUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "Regular Conversation", UserId = _testUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "Other User's Chat", UserId = otherUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await _context.Conversations.AddRangeAsync(conversations);
        var messages = new List<Models.Chat.ChatMessage>
        {
            new() { Id = Guid.NewGuid(), ConversationId = conversations[0].Id, Content = "Hello, this is important", Role = ChatRole.User, UserId = _testUserId, CreatedAt = DateTime.UtcNow, IsSummary = false },
            new() { Id = Guid.NewGuid(), ConversationId = conversations[0].Id, Content = "Hi there, I understand", Role = ChatRole.Assistant, UserId = _testUserId, CreatedAt = DateTime.UtcNow, IsSummary = false },
            new() { Id = Guid.NewGuid(), ConversationId = conversations[1].Id, Content = "Random message", Role = ChatRole.User, UserId = _testUserId, CreatedAt = DateTime.UtcNow, IsSummary = false },
            new() { Id = Guid.NewGuid(), ConversationId = conversations[2].Id, Content = "Hello from other user", Role = ChatRole.User, UserId = otherUserId, CreatedAt = DateTime.UtcNow, IsSummary = false }
        };
        await _context.ChatMessages.AddRangeAsync(messages);
        await _context.SaveChangesAsync();
    }
}
