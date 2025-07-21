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
                // This claim is used by the User.GetUserId() extension method
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

    private async Task SeedDatabase()
    {
        var otherUserId = Guid.NewGuid();

        var conversations = new List<Conversation>
            {
                // User's conversations with different update times
                new() { Id = Guid.NewGuid(), Name = "User Conversation 1", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-2), UpdatedAt = DateTime.UtcNow.AddHours(-2) },
                new() { Id = Guid.NewGuid(), Name = "User Conversation 2 (Most Recent)", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow.AddHours(-1) },
                new() { Id = Guid.NewGuid(), Name = "User Conversation 3", UserId = _testUserId, CreatedAt = DateTime.UtcNow.AddDays(-3), UpdatedAt = DateTime.UtcNow.AddHours(-3) },

                // Another user's conversation that should not be returned
                new() { Id = Guid.NewGuid(), Name = "Other User's Conversation", UserId = otherUserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };

        await _context.Conversations.AddRangeAsync(conversations);
        await _context.SaveChangesAsync();
    }

    [Test]
    public async Task GetConversations_WhenUserHasConversations_ReturnsOkWithCorrectlyOrderedConversations()
    {
        // Arrange
        await SeedDatabase();

        // Act
        var actionResult = await _controller.GetConversations();

        // Assert
        Assert.That(actionResult, Is.Not.Null);
        var conversations = actionResult.Value as List<ConversationListItem>;
        Assert.That(conversations, Is.Not.Null.And.Count.EqualTo(3));

        Assert.Multiple(() =>
        {
            Assert.That(conversations[0].Name, Is.EqualTo("User Conversation 2 (Most Recent)"));
            Assert.That(conversations[1].Name, Is.EqualTo("User Conversation 1"));
            Assert.That(conversations[2].Name, Is.EqualTo("User Conversation 3"));
        });
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
}