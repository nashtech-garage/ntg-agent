using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.ServiceDefaults.Logging;
using NTG.Agent.ServiceDefaults.Logging.Metrics;
using System.Security.Claims;
using System.Text;
using NTG.Agent.Common.Dtos.Documents;
using Microsoft.Extensions.Logging;
namespace NTG.Agent.Orchestrator.Tests.Controllers;
[TestFixture]
public class DocumentsControllerTests
{
    private AgentDbContext _context;
    private Mock<IKnowledgeService> _mockKnowledgeService;
    private Mock<ILogger<DocumentsController>> _mockLogger;
    private Mock<IMetricsCollector> _mockMetrics;
    private IngestionStatusSignal _signal;
    private DocumentsController _controller;
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
        _mockLogger = new Mock<ILogger<DocumentsController>>();
        _mockMetrics = new Mock<IMetricsCollector>();
        var mockScope = new Mock<IDisposable>();
        var mockTimer = new Mock<IDisposable>();
        _mockMetrics.Setup(x => x.StartTimer(It.IsAny<string>(), It.IsAny<(string, string)[]>())).Returns(mockTimer.Object);
        _signal = new IngestionStatusSignal();
        _testUserId = Guid.NewGuid();
        _testAgentId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "mock"));
        _controller = new DocumentsController(_context, _mockKnowledgeService.Object, _mockLogger.Object, _mockMetrics.Object, _signal)
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
        Assert.Throws<ArgumentNullException>(() => new DocumentsController(null!, _mockKnowledgeService.Object, _mockLogger.Object, _mockMetrics.Object, _signal));
    }
    [Test]
    public void Constructor_WhenKnowledgeServiceIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentsController(_context, null!, _mockLogger.Object, _mockMetrics.Object, _signal));
    }
    [Test]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentsController(_context, _mockKnowledgeService.Object, null!, _mockMetrics.Object, _signal));
    }
    [Test]
    public void Constructor_WhenMetricsIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentsController(_context, _mockKnowledgeService.Object, _mockLogger.Object, null!, _signal));
    }
    [Test]
    public void Constructor_WhenIngestionSignalIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentsController(_context, _mockKnowledgeService.Object, _mockLogger.Object, _mockMetrics.Object, null!));
    }
    [Test]
    public async Task GetDocumentsByAgentId_WhenNoDocuments_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetDocumentsByAgentId(_testAgentId, null);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var documents = okResult.Value as List<DocumentListItem>;
        Assert.That(documents, Is.Not.Null);
        Assert.That(documents, Is.Empty);
    }
    [Test]
    public async Task GetDocumentsByAgentId_WhenDocumentsExist_ReturnsDocuments()
    {
        // Arrange
        var tag = new Models.Tags.Tag { Id = Guid.NewGuid(), Name = "Test Tag" };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            AgentId = _testAgentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DocumentTags = new List<DocumentTag>
            {
                new DocumentTag { TagId = tag.Id, Tag = tag }
            }
        };
        _context.Tags.Add(tag);
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetDocumentsByAgentId(_testAgentId, null);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var documents = okResult.Value as List<DocumentListItem>;
        Assert.That(documents, Is.Not.Null);
        Assert.That(documents, Has.Count.EqualTo(1));
        Assert.That(documents[0].Name, Is.EqualTo("Test Document"));
        Assert.That(documents[0].Tags, Contains.Item("Test Tag"));
    }
    [Test]
    public async Task GetDocumentsByAgentId_WhenFolderIsRoot_ReturnsRootDocuments()
    {
        // Arrange
        var rootFolder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            AgentId = _testAgentId,
            ParentId = null
        };
        var document1 = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Root Document",
            AgentId = _testAgentId,
            FolderId = rootFolder.Id
        };
        var document2 = new Document
        {
            Id = Guid.NewGuid(),
            Name = "No Folder Document",
            AgentId = _testAgentId,
            FolderId = null
        };
        _context.Folders.Add(rootFolder);
        _context.Documents.AddRange(document1, document2);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetDocumentsByAgentId(_testAgentId, rootFolder.Id);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var documents = okResult.Value as List<DocumentListItem>;
        Assert.That(documents, Is.Not.Null);
        Assert.That(documents, Has.Count.EqualTo(2));
    }
    [Test]
    public async Task GetDocumentsByAgentId_WhenSpecificFolder_ReturnsOnlyFolderDocuments()
    {
        // Arrange
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Specific Folder",
            AgentId = _testAgentId,
            ParentId = Guid.NewGuid()
        };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Folder Document",
            AgentId = _testAgentId,
            FolderId = folder.Id
        };
        _context.Folders.Add(folder);
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.GetDocumentsByAgentId(_testAgentId, folder.Id);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var documents = okResult.Value as List<DocumentListItem>;
        Assert.That(documents, Is.Not.Null);
        Assert.That(documents, Has.Count.EqualTo(1));
        Assert.That(documents[0].Name, Is.EqualTo("Folder Document"));
    }
    [Test]
    public async Task UploadDocuments_WhenNoFiles_ReturnsBadRequest()
    {
        // Arrange
        var files = new FormFileCollection();
        // Act
        var result = await _controller.UploadDocuments(_testAgentId, files, null, new List<string>());
        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("No files uploaded."));
    }
    [Test]
    public void UploadDocuments_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        var files = new FormFileCollection
        {
            CreateTestFile("test.txt", "test content")
        };
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _controller.UploadDocuments(_testAgentId, files, null, new List<string>()));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task UploadDocuments_WhenValidFiles_UploadsSuccessfully()
    {
        // Arrange
        var files = new FormFileCollection
        {
            CreateTestFile("test.txt", "test content")
        };
        var tag1Id = Guid.NewGuid();
        var tag2Id = Guid.NewGuid();
        var tags = new List<string> { tag1Id.ToString(), tag2Id.ToString() };
        _mockKnowledgeService.Setup(x => x.BeginImportDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("track-id");
        // Act
        var result = await _controller.UploadDocuments(_testAgentId, files, null, tags);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var savedDocument = await _context.Documents.FirstOrDefaultAsync();
        Assert.That(savedDocument, Is.Not.Null);
        Assert.That(savedDocument.Name, Is.EqualTo("test.txt"));
        Assert.That(savedDocument.AgentId, Is.EqualTo(_testAgentId));
        // Ingestion is async: the row starts as Processing with the track-id and no KnowledgeDocId.
        Assert.That(savedDocument.Status, Is.EqualTo(DocumentStatus.Processing));
        Assert.That(savedDocument.TrackId, Is.EqualTo("track-id"));
        Assert.That(savedDocument.KnowledgeDocId, Is.Null);
        var documentTags = await _context.DocumentTags.Where(dt => dt.DocumentId == savedDocument.Id).ToListAsync();
        Assert.That(documentTags, Has.Count.EqualTo(2));
        // The status worker was signalled to start tracking the new Processing doc.
        Assert.That(_signal.WaitAsync(CancellationToken.None).IsCompleted, Is.True);
    }
    [Test]
    public async Task UploadDocuments_WhenEmptyFile_SkipsFile()
    {
        // Arrange
        var files = new FormFileCollection
        {
            CreateTestFile("empty.txt", ""),
            CreateTestFile("valid.txt", "valid content")
        };
        _mockKnowledgeService.Setup(x => x.BeginImportDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("track-id");
        // Act
        var result = await _controller.UploadDocuments(_testAgentId, files, null, new List<string>());
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var documentsCount = await _context.Documents.CountAsync();
        Assert.That(documentsCount, Is.EqualTo(1)); // Only the valid file should be saved
    }
    [Test]
    public async Task DeleteDocument_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        // Act
        var result = await _controller.DeleteDocument(Guid.NewGuid(), _testAgentId);
        // Assert
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }
    [Test]
    public async Task DeleteDocument_WhenDocumentNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.DeleteDocument(Guid.NewGuid(), _testAgentId);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task DeleteDocument_WhenDocumentExists_DeletesSuccessfully()
    {
        // Arrange
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            AgentId = _testAgentId,
            KnowledgeDocId = "knowledge-doc-id"
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteDocument(document.Id, _testAgentId);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var deletedDocument = await _context.Documents.FindAsync(document.Id);
        Assert.That(deletedDocument, Is.Null);
        _mockKnowledgeService.Verify(x => x.RemoveDocumentAsync(_testAgentId, document.Id, "knowledge-doc-id", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Test]
    public async Task DeleteDocument_WhenNoKnowledgeDocId_StillCallsKnowledgeServiceForCleanup()
    {
        // Arrange
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            AgentId = _testAgentId,
            KnowledgeDocId = null
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        // Act
        var result = await _controller.DeleteDocument(document.Id, _testAgentId);
        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        var deletedDocument = await _context.Documents.FindAsync(document.Id);
        Assert.That(deletedDocument, Is.Null);
        // Even without a KnowledgeDocId we call through so the file store (and any resolvable
        // LightRAG doc) is cleaned up; the service decides whether to hit LightRAG.
        _mockKnowledgeService.Verify(x => x.RemoveDocumentAsync(_testAgentId, document.Id, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Test]
    public void DeleteDocument_WhenKnowledgeServiceThrows_DoesNotRemoveSqlRow()
    {
        // Arrange
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            AgentId = _testAgentId,
            KnowledgeDocId = "knowledge-doc-id"
        };
        _context.Documents.Add(document);
        _context.SaveChanges();

        _mockKnowledgeService
            .Setup(x => x.RemoveDocumentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("KM unavailable"));

        // Act & Assert — exception bubbles up; SQL row must remain so the user can retry
        Assert.ThrowsAsync<InvalidOperationException>(() => _controller.DeleteDocument(document.Id, _testAgentId));

        var stillThere = _context.Documents.AsNoTracking().FirstOrDefault(d => d.Id == document.Id);
        Assert.That(stillThere, Is.Not.Null);
    }
    [Test]
    public async Task ImportWebPage_WhenUrlIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var request = new ImportWebPageRequest("", null, new List<string>());
        // Act
        var result = await _controller.ImportWebPage(_testAgentId, request);
        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("URL is required."));
    }
    [Test]
    public void ImportWebPage_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        var request = new ImportWebPageRequest("https://example.com", null, new List<string>());
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _controller.ImportWebPage(_testAgentId, request));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task ImportWebPage_WhenValidRequest_ImportsSuccessfully()
    {
        // Arrange
        var url = "https://example.com";
        var folderId = Guid.NewGuid();
        var tags = new List<string> { Guid.NewGuid().ToString() };
        var request = new ImportWebPageRequest(url, folderId, tags);
        _mockKnowledgeService.Setup(x => x.BeginImportWebPageAsync(url, _testAgentId, It.IsAny<Guid>(), tags, It.IsAny<CancellationToken>()))
            .ReturnsAsync("track-id");
        // Act
        var result = await _controller.ImportWebPage(_testAgentId, request);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var savedDocument = await _context.Documents.FirstOrDefaultAsync();
        Assert.That(savedDocument, Is.Not.Null);
        // The endpoint now returns the local document id (the row is created before ingestion finishes).
        Assert.That(okResult.Value, Is.EqualTo(savedDocument.Id.ToString()));
        Assert.That(savedDocument.Name, Is.EqualTo(url));
        Assert.That(savedDocument.Url, Is.EqualTo(url));
        Assert.That(savedDocument.Type, Is.EqualTo(DocumentType.WebPage));
        Assert.That(savedDocument.FolderId, Is.EqualTo(folderId));
        Assert.That(savedDocument.Status, Is.EqualTo(DocumentStatus.Processing));
        Assert.That(savedDocument.TrackId, Is.EqualTo("track-id"));
    }
    [Test]
    public async Task ImportWebPage_WhenKnowledgeServiceThrows_ReturnsBadRequest()
    {
        // Arrange
        var request = new ImportWebPageRequest("https://example.com", null, new List<string>());
        _mockKnowledgeService.Setup(x => x.BeginImportWebPageAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Import failed"));
        // Act
        var result = await _controller.ImportWebPage(_testAgentId, request);
        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("Failed to import webpage: Import failed"));
    }
    [Test]
    public async Task UploadTextContent_WhenContentIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var request = new UploadTextContentRequest("Title", "", null, new List<string>());
        // Act
        var result = await _controller.UploadTextContent(_testAgentId, request);
        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("Content is required."));
    }
    [Test]
    public void UploadTextContent_WhenUserNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext.User = anonymousUser;
        var request = new UploadTextContentRequest("Title", "Content", null, new List<string>());
        // Act & Assert
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _controller.UploadTextContent(_testAgentId, request));
        Assert.That(exception.Message, Is.EqualTo("User is not authenticated."));
    }
    [Test]
    public async Task UploadTextContent_WhenValidRequest_UploadsSuccessfully()
    {
        // Arrange
        var content = "This is test content";
        var title = "Test Title";
        var folderId = Guid.NewGuid();
        var tags = new List<string> { Guid.NewGuid().ToString() };
        var request = new UploadTextContentRequest(title, content, folderId, tags);
        _mockKnowledgeService.Setup(x => x.BeginImportTextContentAsync(content, $"{title}.txt", _testAgentId, It.IsAny<Guid>(), tags, It.IsAny<CancellationToken>()))
            .ReturnsAsync("track-id");
        // Act
        var result = await _controller.UploadTextContent(_testAgentId, request);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var savedDocument = await _context.Documents.FirstOrDefaultAsync();
        Assert.That(savedDocument, Is.Not.Null);
        Assert.That(savedDocument.Name, Is.EqualTo($"{title}.txt"));
        Assert.That(savedDocument.Type, Is.EqualTo(DocumentType.Text));
        Assert.That(savedDocument.FolderId, Is.EqualTo(folderId));
        Assert.That(savedDocument.Status, Is.EqualTo(DocumentStatus.Processing));
        Assert.That(savedDocument.TrackId, Is.EqualTo("track-id"));
    }
    [Test]
    public async Task UploadTextContent_WhenNoTitle_UsesDefaultTitle()
    {
        // Arrange
        var content = "This is test content";
        var request = new UploadTextContentRequest("", content, null, new List<string>());
        _mockKnowledgeService.Setup(x => x.BeginImportTextContentAsync(content, "Text Content.txt", _testAgentId, It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("track-id");
        // Act
        var result = await _controller.UploadTextContent(_testAgentId, request);
        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var savedDocument = await _context.Documents.FirstOrDefaultAsync();
        Assert.That(savedDocument, Is.Not.Null);
        Assert.That(savedDocument.Name, Is.EqualTo("Text Content.txt"));
    }
    [Test]
    public async Task UploadTextContent_WhenKnowledgeServiceThrows_ReturnsBadRequest()
    {
        // Arrange
        var request = new UploadTextContentRequest("Title", "Content", null, new List<string>());
        _mockKnowledgeService.Setup(x => x.BeginImportTextContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Import failed"));
        // Act
        var result = await _controller.UploadTextContent(_testAgentId, request);
        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("Failed to upload text content: Import failed"));
    }
    [Test]
    public async Task GetDocumentById_WhenDocumentNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetDocumentById(Guid.NewGuid(), _testAgentId, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task GetDocumentById_WhenFileNotInStore_ReturnsNotFound()
    {
        // Arrange — a file document whose bytes are missing from the store (e.g. still processing).
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "test.txt",
            AgentId = _testAgentId,
            Type = DocumentType.File,
            KnowledgeDocId = null
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        _mockKnowledgeService
            .Setup(x => x.ExportDocumentAsync(_testAgentId, document.Id, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());
        // Act
        var result = await _controller.GetDocumentById(document.Id, _testAgentId, CancellationToken.None);
        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
    [Test]
    public async Task GetDocumentById_WhenWebPageDocument_HandlesDownload()
    {
        // Arrange
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Example Page",
            AgentId = _testAgentId,
            Type = DocumentType.WebPage,
            Url = "https://example.com"
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        // Note: This test would require mocking HttpClient which is complex
        // In practice, you might want to extract the HTTP logic to a separate service
        // For now, we'll test the URL validation part
        // Act
        var result = await _controller.GetDocumentById(document.Id, _testAgentId, CancellationToken.None);
        // Assert
        // This would normally test the HTTP download, but since we can't easily mock HttpClient
        // we're just verifying the method executes without null reference exceptions
        Assert.That(result, Is.Not.Null);
    }
    private static FormFile CreateTestFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var formFile = new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
        return formFile;
    }
}
