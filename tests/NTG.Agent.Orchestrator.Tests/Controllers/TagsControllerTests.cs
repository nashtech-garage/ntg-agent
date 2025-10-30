using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NTG.Agent.Common.Dtos.Tags;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Models.Tags;
using System.Security.Claims;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class TagsControllerTests
{
    private AgentDbContext _dbContext = null!;
    private TagsController _controller = null!;
    private Mock<ILogger<TagsController>> _mockLogger = null!;
    [SetUp]
    public void Setup()
    {
        // Create in-memory database with unique name for each test
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;
        _dbContext = new AgentDbContext(options);
        // Setup mocks
        _mockLogger = new Mock<ILogger<TagsController>>();
        // Create controller
        _controller = new TagsController(_dbContext, _mockLogger.Object);
        // Setup HTTP context with authenticated admin user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }
    [TearDown]
    public void TearDown()
    {
        _dbContext?.Dispose();
    }
    [Test]
    public async Task GetTags_WhenNoTags_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetTags(null, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tags = okResult!.Value as IEnumerable<TagDto>;
        Assert.That(tags, Is.Empty);
    }
    [Test]
    public async Task GetTags_WhenTagsExist_ReturnsAllTags()
    {
        // Arrange
        var tag1 = new Tag { Name = "Important" };
        var tag2 = new Tag { Name = "Archive" };
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetTags(null, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tags = okResult!.Value as IEnumerable<TagDto>;
        Assert.That(tags!.Count(), Is.EqualTo(2));
        Assert.That(tags!.Any(t => t.Name == "Important"), Is.True);
        Assert.That(tags!.Any(t => t.Name == "Archive"), Is.True);
    }
    [Test]
    public async Task GetTags_WhenSearchQuery_ReturnsFilteredTags()
    {
        // Arrange
        var tag1 = new Tag { Name = "Important" };
        var tag2 = new Tag { Name = "Archive" };
        var tag3 = new Tag { Name = "Urgent" };
        _dbContext.Tags.AddRange(tag1, tag2, tag3);
        await _dbContext.SaveChangesAsync();
        // Act - using case-insensitive search that works with in-memory database
        var result = await _controller.GetTags("Import", CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tags = okResult!.Value as IEnumerable<TagDto>;
        Assert.That(tags!.Count(), Is.EqualTo(1));
        Assert.That(tags!.First().Name, Is.EqualTo("Important"));
    }
    [Test]
    public async Task GetTags_WhenSearchQueryNoMatch_ReturnsEmptyList()
    {
        // Arrange
        var tag1 = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag1);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetTags("nonexistent", CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tags = okResult!.Value as IEnumerable<TagDto>;
        Assert.That(tags, Is.Empty);
    }
    [Test]
    public async Task GetTags_OrdersByName()
    {
        // Arrange
        var tag1 = new Tag { Name = "Zebra" };
        var tag2 = new Tag { Name = "Alpha" };
        var tag3 = new Tag { Name = "Beta" };
        _dbContext.Tags.AddRange(tag1, tag2, tag3);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetTags(null, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tags = (okResult!.Value as IEnumerable<TagDto>)!.ToList();
        Assert.That(tags[0].Name, Is.EqualTo("Alpha"));
        Assert.That(tags[1].Name, Is.EqualTo("Beta"));
        Assert.That(tags[2].Name, Is.EqualTo("Zebra"));
    }
    [Test]
    public async Task GetTagById_WhenTagExists_ReturnsTag()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetTagById(tag.Id, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var returnedTag = okResult!.Value as TagDto;
        Assert.That(returnedTag!.Id, Is.EqualTo(tag.Id));
        Assert.That(returnedTag.Name, Is.EqualTo("Important"));
    }
    [Test]
    public async Task GetTagById_WhenTagNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetTagById(Guid.NewGuid(), CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task CreateTag_WhenValidDto_CreatesTag()
    {
        // Arrange
        var dto = new TagCreateDto("Important");
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        var returnedTag = createdResult!.Value as TagDto;
        Assert.That(returnedTag!.Name, Is.EqualTo("Important"));
        var tagInDb = await _dbContext.Tags.FirstOrDefaultAsync(t => t.Name == "Important");
        Assert.That(tagInDb, Is.Not.Null);
    }
    [Test]
    public async Task CreateTag_WhenNameIsNull_ReturnsBadRequest()
    {
        // Arrange
        var dto = new TagCreateDto(null!);
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result.Result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Tag Name is required."));
    }
    [Test]
    public async Task CreateTag_WhenNameIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var dto = new TagCreateDto("");
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result.Result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Tag Name is required."));
    }
    [Test]
    public async Task CreateTag_WhenNameIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        var dto = new TagCreateDto("   ");
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result.Result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Tag Name is required."));
    }
    [Test]
    public async Task CreateTag_WhenTagNameExists_ReturnsConflict()
    {
        // Arrange
        var existingTag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(existingTag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagCreateDto("Important");
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = result.Result as ConflictObjectResult;
        Assert.That(conflictResult!.Value, Is.EqualTo("Tag with name 'Important' already exists."));
    }
    [Test]
    public async Task CreateTag_TrimsWhitespace()
    {
        // Arrange
        var dto = new TagCreateDto("  Important  ");
        // Act
        var result = await _controller.CreateTag(dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        var returnedTag = createdResult!.Value as TagDto;
        Assert.That(returnedTag!.Name, Is.EqualTo("Important"));
    }
    [Test]
    public async Task UpdateTag_WhenValidDto_UpdatesTag()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto("VeryImportant");
        // Act
        var result = await _controller.UpdateTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedTag = await _dbContext.Tags.FindAsync(tag.Id);
        Assert.That(updatedTag!.Name, Is.EqualTo("VeryImportant"));
    }
    [Test]
    public async Task UpdateTag_WhenTagNotFound_ReturnsNotFound()
    {
        // Arrange
        var dto = new TagUpdateDto("Updated");
        // Act
        var result = await _controller.UpdateTag(Guid.NewGuid(), dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task UpdateTag_WhenNameIsNull_ReturnsBadRequest()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto(null!);
        // Act
        var result = await _controller.UpdateTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Tag Name is required."));
    }
    [Test]
    public async Task UpdateTag_WhenNameIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto("");
        // Act
        var result = await _controller.UpdateTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Tag Name is required."));
    }
    [Test]
    public async Task UpdateTag_WhenNewNameAlreadyExists_ReturnsConflict()
    {
        // Arrange
        var tag1 = new Tag { Name = "Important" };
        var tag2 = new Tag { Name = "Archive" };
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto("Archive");
        // Act
        var result = await _controller.UpdateTag(tag1.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult!.Value, Is.EqualTo("Tag with name 'Archive' already exists."));
    }
    [Test]
    public async Task UpdateTag_WhenSameName_SucceedsWithoutConflict()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto("Important");
        // Act
        var result = await _controller.UpdateTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
    }
    [Test]
    public async Task UpdateTag_TrimsWhitespace()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        var dto = new TagUpdateDto("  VeryImportant  ");
        // Act
        var result = await _controller.UpdateTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var updatedTag = await _dbContext.Tags.FindAsync(tag.Id);
        Assert.That(updatedTag!.Name, Is.EqualTo("VeryImportant"));
    }
    [Test]
    public async Task DeleteTag_WhenTagExists_DeletesTag()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteTag(tag.Id, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var deletedTag = await _dbContext.Tags.FindAsync(tag.Id);
        Assert.That(deletedTag, Is.Null);
    }
    [Test]
    public async Task DeleteTag_WhenTagNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.DeleteTag(Guid.NewGuid(), CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task DeleteTag_WhenTagHasDocuments_ReturnsBadRequest()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        // Add a document tag relationship
        var documentTag = new DocumentTag { TagId = tag.Id, DocumentId = Guid.NewGuid() };
        _dbContext.DocumentTags.Add(documentTag);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteTag(tag.Id, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        var badResult = result as BadRequestObjectResult;
        Assert.That(badResult!.Value, Is.EqualTo("Cannot delete tag. It is currently associated with one or more documents. Please remove the tag from all documents before deleting it."));
        // Verify tag still exists
        var tagInDb = await _dbContext.Tags.FindAsync(tag.Id);
        Assert.That(tagInDb, Is.Not.Null);
    }
    [Test]
    public async Task GetAvailableRoles_WhenNoRoles_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetAvailableRoles(CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var roles = okResult!.Value as IEnumerable<RoleDto>;
        Assert.That(roles, Is.Empty);
    }
    [Test]
    public async Task GetAvailableRoles_WhenRolesExist_ReturnsAllRoles()
    {
        // Arrange
        var role1 = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var role2 = new Role { Id = Guid.NewGuid(), Name = "User" };
        _dbContext.Roles.AddRange(role1, role2);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetAvailableRoles(CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var roles = okResult!.Value as IEnumerable<RoleDto>;
        Assert.That(roles!.Count(), Is.EqualTo(2));
        Assert.That(roles!.Any(r => r.Name == "Admin"), Is.True);
        Assert.That(roles!.Any(r => r.Name == "User"), Is.True);
    }
    [Test]
    public async Task GetAvailableRoles_OrdersByName()
    {
        // Arrange
        var role1 = new Role { Id = Guid.NewGuid(), Name = "Zebra" };
        var role2 = new Role { Id = Guid.NewGuid(), Name = "Alpha" };
        _dbContext.Roles.AddRange(role1, role2);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetAvailableRoles(CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var roles = (okResult!.Value as IEnumerable<RoleDto>)!.ToList();
        Assert.That(roles[0].Name, Is.EqualTo("Alpha"));
        Assert.That(roles[1].Name, Is.EqualTo("Zebra"));
    }
    [Test]
    public async Task GetRolesForTag_WhenTagNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetRolesForTag(Guid.NewGuid(), CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        var notFoundResult = result.Result as NotFoundObjectResult;
        Assert.That(notFoundResult!.Value!.ToString(), Does.Contain("not found"));
    }
    [Test]
    public async Task GetRolesForTag_WhenTagExistsWithNoRoles_ReturnsEmptyList()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetRolesForTag(tag.Id, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tagRoles = okResult!.Value as IEnumerable<TagRoleDto>;
        Assert.That(tagRoles, Is.Empty);
    }
    [Test]
    public async Task GetRolesForTag_WhenTagHasRoles_ReturnsRoles()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        var role1 = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var role2 = new Role { Id = Guid.NewGuid(), Name = "User" };
        _dbContext.Tags.Add(tag);
        _dbContext.Roles.AddRange(role1, role2);
        await _dbContext.SaveChangesAsync();
        var tagRole1 = new TagRole { TagId = tag.Id, RoleId = role1.Id };
        var tagRole2 = new TagRole { TagId = tag.Id, RoleId = role2.Id };
        _dbContext.TagRoles.AddRange(tagRole1, tagRole2);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.GetRolesForTag(tag.Id, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        var tagRoles = okResult!.Value as IEnumerable<TagRoleDto>;
        Assert.That(tagRoles!.Count(), Is.EqualTo(2));
        Assert.That(tagRoles!.Any(tr => tr.RoleId == role1.Id), Is.True);
        Assert.That(tagRoles!.Any(tr => tr.RoleId == role2.Id), Is.True);
    }
    [Test]
    public async Task AttachRoleToTag_WhenTagNotFound_ReturnsNotFound()
    {
        // Arrange
        var dto = new TagRoleAttachDto(Guid.NewGuid());
        // Act
        var result = await _controller.AttachRoleToTag(Guid.NewGuid(), dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        var notFoundResult = result.Result as NotFoundObjectResult;
        Assert.That(notFoundResult!.Value!.ToString(), Does.Contain("not found"));
    }
    [Test]
    public async Task AttachRoleToTag_WhenValidDto_CreatesMapping()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        _dbContext.Tags.Add(tag);
        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync();
        var dto = new TagRoleAttachDto(role.Id);
        // Act
        var result = await _controller.AttachRoleToTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result.Result as CreatedAtActionResult;
        var tagRoleDto = createdResult!.Value as TagRoleDto;
        Assert.That(tagRoleDto!.TagId, Is.EqualTo(tag.Id));
        Assert.That(tagRoleDto.RoleId, Is.EqualTo(role.Id));
        var mappingInDb = await _dbContext.TagRoles.FirstOrDefaultAsync(tr => tr.TagId == tag.Id && tr.RoleId == role.Id);
        Assert.That(mappingInDb, Is.Not.Null);
    }
    [Test]
    public async Task AttachRoleToTag_WhenMappingAlreadyExists_ReturnsConflict()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        _dbContext.Tags.Add(tag);
        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync();
        var existingMapping = new TagRole { TagId = tag.Id, RoleId = role.Id };
        _dbContext.TagRoles.Add(existingMapping);
        await _dbContext.SaveChangesAsync();
        var dto = new TagRoleAttachDto(role.Id);
        // Act
        var result = await _controller.AttachRoleToTag(tag.Id, dto, CancellationToken.None);
        // Assert
        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = result.Result as ConflictObjectResult;
        Assert.That(conflictResult!.Value, Is.EqualTo("This tag/role mapping already exists."));
    }
    [Test]
    public async Task DetachRoleFromTag_WhenMappingExists_RemovesMapping()
    {
        // Arrange
        var tag = new Tag { Name = "Important" };
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        _dbContext.Tags.Add(tag);
        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync();
        var mapping = new TagRole { TagId = tag.Id, RoleId = role.Id };
        _dbContext.TagRoles.Add(mapping);
        await _dbContext.SaveChangesAsync();
        // Act
        var result = await _controller.DetachRoleFromTag(tag.Id, role.Id, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var mappingInDb = await _dbContext.TagRoles.FirstOrDefaultAsync(tr => tr.TagId == tag.Id && tr.RoleId == role.Id);
        Assert.That(mappingInDb, Is.Null);
    }
    [Test]
    public async Task DetachRoleFromTag_WhenMappingNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.DetachRoleFromTag(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public void GetTags_RequiresAdminRole()
    {
        // This test verifies that the controller has [Authorize(Roles = "Admin")] attribute
        var controllerType = typeof(TagsController);
        var authorizeAttribute = controllerType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();
        Assert.That(authorizeAttribute, Is.Not.Null);
        Assert.That(authorizeAttribute!.Roles, Is.EqualTo("Admin"));
    }
}
