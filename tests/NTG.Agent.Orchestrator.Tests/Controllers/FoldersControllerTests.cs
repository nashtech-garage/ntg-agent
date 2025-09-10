using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Shared.Dtos.Folders;
using System.Security.Claims;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class FoldersControllerTests
{
    private AgentDbContext _context;
    private Mock<IKnowledgeService> _mockKnowledgeService;
    private FoldersController _controller;
    private Guid _testUserId;
    private Guid _testAgentId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);

        _mockKnowledgeService = new Mock<IKnowledgeService>();
        
        _testUserId = Guid.NewGuid();
        _testAgentId = Guid.NewGuid();

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "mock"));

        _controller = new FoldersController(_context, _mockKnowledgeService.Object)
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

    #region Constructor Tests

    [Test]
    public void Constructor_WhenAgentDbContextIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FoldersController(null!, _mockKnowledgeService.Object));
    }

    [Test]
    public void Constructor_WhenKnowledgeServiceIsNull_DoesNotThrow()
    {
        // The knowledge service can be null based on the constructor
        Assert.DoesNotThrow(() => new FoldersController(_context, null!));
    }

    [Test]
    public void Constructor_WhenValidParameters_CreatesInstance()
    {
        var controller = new FoldersController(_context, _mockKnowledgeService.Object);
        Assert.That(controller, Is.Not.Null);
    }

    #endregion

    #region GetFolders Tests

    [Test]
    public async Task GetFolders_WhenNoFolders_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetFolders(null);

        // Assert
        var actionResult = result as ActionResult<IEnumerable<Folder>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Empty);
    }

    [Test]
    public async Task GetFolders_WhenFoldersExist_ReturnsAllFolders()
    {
        // Arrange
        var folder1 = new Folder { Id = Guid.NewGuid(), Name = "Folder 1", AgentId = _testAgentId };
        var folder2 = new Folder { Id = Guid.NewGuid(), Name = "Folder 2", AgentId = Guid.NewGuid() };

        _context.Folders.AddRange(folder1, folder2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolders(null);

        // Assert
        var actionResult = result as ActionResult<IEnumerable<Folder>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value.Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task GetFolders_WhenAgentIdProvided_ReturnsFilteredFolders()
    {
        // Arrange
        var folder1 = new Folder { Id = Guid.NewGuid(), Name = "Folder 1", AgentId = _testAgentId };
        var folder2 = new Folder { Id = Guid.NewGuid(), Name = "Folder 2", AgentId = Guid.NewGuid() };

        _context.Folders.AddRange(folder1, folder2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolders(_testAgentId);

        // Assert
        var actionResult = result as ActionResult<IEnumerable<Folder>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value.Count(), Is.EqualTo(1));
        Assert.That(actionResult.Value.First().AgentId, Is.EqualTo(_testAgentId));
    }

    [Test]
    public async Task GetFolders_WhenFoldersHaveChildren_IncludesChildren()
    {
        // Arrange
        var parentFolder = new Folder { Id = Guid.NewGuid(), Name = "Parent", AgentId = _testAgentId };
        var childFolder = new Folder { Id = Guid.NewGuid(), Name = "Child", AgentId = _testAgentId, ParentId = parentFolder.Id };

        _context.Folders.AddRange(parentFolder, childFolder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolders(_testAgentId);

        // Assert
        var actionResult = result as ActionResult<IEnumerable<Folder>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        
        var folders = actionResult.Value.ToList();
        Assert.That(folders, Has.Count.EqualTo(2));
        
        var parent = folders.FirstOrDefault(f => f.Name == "Parent");
        Assert.That(parent, Is.Not.Null);
        Assert.That(parent.Children, Is.Not.Null);
    }

    [Test]
    public async Task GetFolders_WhenFoldersHaveDocuments_IncludesDocuments()
    {
        // Arrange
        var folder = new Folder { Id = Guid.NewGuid(), Name = "Folder", AgentId = _testAgentId };
        var document = new Document { Id = Guid.NewGuid(), Name = "Document", AgentId = _testAgentId, FolderId = folder.Id };

        _context.Folders.Add(folder);
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolders(_testAgentId);

        // Assert
        var actionResult = result as ActionResult<IEnumerable<Folder>>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        
        var folders = actionResult.Value.ToList();
        Assert.That(folders, Has.Count.EqualTo(1));
        
        var folderWithDocs = folders.First();
        Assert.That(folderWithDocs.Documents, Is.Not.Null);
    }

    #endregion

    #region GetFolder Tests

    [Test]
    public async Task GetFolder_WhenFolderNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetFolder(Guid.NewGuid());

        // Assert
        var actionResult = result as ActionResult<Folder>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task GetFolder_WhenFolderExists_ReturnsFolder()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var folder = new Folder { Id = folderId, Name = "Test Folder", AgentId = _testAgentId };

        _context.Folders.Add(folder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolder(folderId);

        // Assert
        var actionResult = result as ActionResult<Folder>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value.Id, Is.EqualTo(folderId));
        Assert.That(actionResult.Value.Name, Is.EqualTo("Test Folder"));
    }

    [Test]
    public async Task GetFolder_WhenFolderHasChildren_IncludesChildren()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var parentFolder = new Folder { Id = parentId, Name = "Parent", AgentId = _testAgentId };
        var childFolder = new Folder { Id = Guid.NewGuid(), Name = "Child", AgentId = _testAgentId, ParentId = parentId };

        _context.Folders.AddRange(parentFolder, childFolder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetFolder(parentId);

        // Assert
        var actionResult = result as ActionResult<Folder>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value.Children, Is.Not.Null);
        Assert.That(actionResult.Value.Children, Has.Count.EqualTo(1));
    }

    #endregion

    #region CreateFolder Tests

    [Test]
    public async Task CreateFolder_WhenModelStateInvalid_ReturnsBadRequest()
    {
        // Arrange
        _controller.ModelState.AddModelError("Name", "Name is required");
        var dto = new CreateFolderDto { Name = "", AgentId = _testAgentId };

        // Act
        var result = await _controller.CreateFolder(dto);

        // Assert
        var actionResult = result as ActionResult<Folder>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public void CreateFolder_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;

        var dto = new CreateFolderDto { Name = "Test Folder", AgentId = _testAgentId };

        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.CreateFolder(dto));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }

    [Test]
    public async Task CreateFolder_WhenValidDto_CreatesFolder()
    {
        // Arrange
        var dto = new CreateFolderDto
        {
            Name = "Test Folder",
            AgentId = _testAgentId,
            ParentId = null
        };

        // Act
        var result = await _controller.CreateFolder(dto);

        // Assert
        var actionResult = result as ActionResult<Folder>;
        Assert.That(actionResult, Is.Not.Null);
        Assert.That(actionResult.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdResult = actionResult.Result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        
        var createdFolder = createdResult.Value as Folder;
        Assert.That(createdFolder, Is.Not.Null);
        Assert.That(createdFolder.Name, Is.EqualTo("Test Folder"));
        Assert.That(createdFolder.AgentId, Is.EqualTo(_testAgentId));
        Assert.That(createdFolder.CreatedByUserId, Is.EqualTo(_testUserId));
        Assert.That(createdFolder.UpdatedByUserId, Is.EqualTo(_testUserId));

        // Verify it was saved to database
        var savedFolder = await _context.Folders.FindAsync(createdFolder.Id);
        Assert.That(savedFolder, Is.Not.Null);
    }

    [Test]
    public async Task CreateFolder_WhenParentIdProvided_SetsParentId()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var dto = new CreateFolderDto
        {
            Name = "Child Folder",
            AgentId = _testAgentId,
            ParentId = parentId
        };

        // Act
        var result = await _controller.CreateFolder(dto);

        // Assert
        var actionResult = result as ActionResult<Folder>;
        var createdResult = actionResult?.Result as CreatedAtActionResult;
        var createdFolder = createdResult?.Value as Folder;
        
        Assert.That(createdFolder, Is.Not.Null);
        Assert.That(createdFolder.ParentId, Is.EqualTo(parentId));
    }

    #endregion

    #region UpdateFolder Tests

    [Test]
    public async Task UpdateFolder_WhenIdMismatch_ReturnsBadRequest()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var dto = new UpdateFolderDto
        {
            Id = Guid.NewGuid(), // Different ID
            Name = "Updated Folder",
            AgentId = _testAgentId
        };

        // Act
        var result = await _controller.UpdateFolder(folderId, dto);

        // Assert
        Assert.That(result, Is.TypeOf<BadRequestResult>());
    }

    [Test]
    public void UpdateFolder_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;

        var folderId = Guid.NewGuid();
        var dto = new UpdateFolderDto
        {
            Id = folderId,
            Name = "Updated Folder",
            AgentId = _testAgentId
        };

        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.UpdateFolder(folderId, dto));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }

    [Test]
    public async Task UpdateFolder_WhenFolderNotFound_ReturnsNotFound()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var dto = new UpdateFolderDto
        {
            Id = folderId,
            Name = "Updated Folder",
            AgentId = _testAgentId
        };

        // Act
        var result = await _controller.UpdateFolder(folderId, dto);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task UpdateFolder_WhenValidRequest_UpdatesFolder()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var folder = new Folder
        {
            Id = folderId,
            Name = "Original Name",
            AgentId = _testAgentId,
            CreatedByUserId = _testUserId,
            UpdatedByUserId = _testUserId
        };

        _context.Folders.Add(folder);
        await _context.SaveChangesAsync();

        var dto = new UpdateFolderDto
        {
            Id = folderId,
            Name = "Updated Name",
            AgentId = _testAgentId,
            ParentId = Guid.NewGuid()
        };

        // Act
        var result = await _controller.UpdateFolder(folderId, dto);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());

        var updatedFolder = await _context.Folders.FindAsync(folderId);
        Assert.That(updatedFolder, Is.Not.Null);
        Assert.That(updatedFolder.Name, Is.EqualTo("Updated Name"));
        Assert.That(updatedFolder.ParentId, Is.EqualTo(dto.ParentId));
        Assert.That(updatedFolder.UpdatedByUserId, Is.EqualTo(_testUserId));
    }

    #endregion

    #region DeleteFolder Tests

    [Test]
    public async Task DeleteFolder_WhenFolderNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.DeleteFolder(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task DeleteFolder_WhenFolderNotDeletable_ReturnsBadRequest()
    {
        // Arrange
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "System Folder",
            AgentId = _testAgentId,
            IsDeletable = false
        };

        _context.Folders.Add(folder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteFolder(folder.Id);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("This folder cannot be deleted as it is a system folder."));
    }

    [Test]
    public async Task DeleteFolder_WhenFolderHasChildren_ReturnsBadRequest()
    {
        // Arrange
        var parentFolder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Parent Folder",
            AgentId = _testAgentId,
            IsDeletable = true
        };

        var childFolder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Child Folder",
            AgentId = _testAgentId,
            ParentId = parentFolder.Id
        };

        _context.Folders.AddRange(parentFolder, childFolder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteFolder(parentFolder.Id);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("Cannot delete folder with child folders."));
    }

    [Test]
    public async Task DeleteFolder_WhenFolderHasDocuments_DeletesFolderAndDocuments()
    {
        // Arrange
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Folder with docs",
            AgentId = _testAgentId,
            IsDeletable = true
        };

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Document",
            AgentId = _testAgentId,
            FolderId = folder.Id,
            KnowledgeDocId = "knowledge-doc-id"
        };

        _context.Folders.Add(folder);
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteFolder(folder.Id);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());

        var deletedFolder = await _context.Folders.FindAsync(folder.Id);
        Assert.That(deletedFolder, Is.Null);

        var deletedDocument = await _context.Documents.FindAsync(document.Id);
        Assert.That(deletedDocument, Is.Null);

        _mockKnowledgeService.Verify(x => x.RemoveDocumentAsync("knowledge-doc-id", _testAgentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteFolder_WhenValidEmptyFolder_DeletesSuccessfully()
    {
        // Arrange
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Empty Folder",
            AgentId = _testAgentId,
            IsDeletable = true
        };

        _context.Folders.Add(folder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteFolder(folder.Id);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());

        var deletedFolder = await _context.Folders.FindAsync(folder.Id);
        Assert.That(deletedFolder, Is.Null);
    }

    [Test]
    public async Task DeleteFolder_WhenDocumentHasNoKnowledgeDocId_DeletesWithoutKnowledgeService()
    {
        // Arrange
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Folder with docs",
            AgentId = _testAgentId,
            IsDeletable = true
        };

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Document",
            AgentId = _testAgentId,
            FolderId = folder.Id,
            KnowledgeDocId = null
        };

        _context.Folders.Add(folder);
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteFolder(folder.Id);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());

        _mockKnowledgeService.Verify(x => x.RemoveDocumentAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
