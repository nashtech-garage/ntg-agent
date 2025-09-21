using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Moq;
using NTG.Agent.Orchestrator.Services.Knowledge;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class KernelMemoryKnowledgeTests
{
    private Mock<IKernelMemory> _mockKernelMemory = null!;
    private Mock<ILogger<KernelMemoryKnowledge>> _mockLogger = null!;
    private KernelMemoryKnowledge _service = null!;
    private Guid _testAgentId;

    [SetUp]
    public void Setup()
    {
        _mockKernelMemory = new Mock<IKernelMemory>();
        _mockLogger = new Mock<ILogger<KernelMemoryKnowledge>>();
        _testAgentId = Guid.NewGuid();
        _service = new KernelMemoryKnowledge(_mockKernelMemory.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WhenKernelMemoryIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new KernelMemoryKnowledge(null!, _mockLogger.Object));
        
        Assert.That(exception.ParamName, Is.EqualTo("kernelMemory"));
    }

    [Test]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new KernelMemoryKnowledge(_mockKernelMemory.Object, null!));
        
        Assert.That(exception.ParamName, Is.EqualTo("logger"));
    }

    [Test]
    public void Constructor_WhenValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var service = new KernelMemoryKnowledge(_mockKernelMemory.Object, _mockLogger.Object);

        // Assert
        Assert.That(service, Is.Not.Null);
        Assert.That(service, Is.InstanceOf<IKnowledgeService>());
    }

    #endregion

    #region ImportWebPageAsync Validation Tests

    [Test]
    public void ImportWebPageAsync_WithNullUrl_ThrowsArgumentException()
    {
        // Arrange
        string? url = null;
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportWebPageAsync(url!, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("url"));
        Assert.That(exception.Message, Does.Contain("Invalid URL provided."));
    }

    [Test]
    public void ImportWebPageAsync_WithEmptyUrl_ThrowsArgumentException()
    {
        // Arrange
        var url = "";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportWebPageAsync(url, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("url"));
        Assert.That(exception.Message, Does.Contain("Invalid URL provided."));
    }

    [Test]
    public void ImportWebPageAsync_WithInvalidUrl_ThrowsArgumentException()
    {
        // Arrange
        var url = "not-a-valid-url";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportWebPageAsync(url, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("url"));
        Assert.That(exception.Message, Does.Contain("Invalid URL provided."));
    }

    [Test]
    public void ImportWebPageAsync_WithFtpUrl_ThrowsArgumentException()
    {
        // Arrange
        var url = "ftp://example.com/file.txt";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportWebPageAsync(url, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("url"));
        Assert.That(exception.Message, Does.Contain("Invalid URL provided."));
    }

    [Test]
    public void ImportWebPageAsync_WithFileUrl_ThrowsArgumentException()
    {
        // Arrange
        var url = "file:///c:/temp/test.txt";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportWebPageAsync(url, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("url"));
        Assert.That(exception.Message, Does.Contain("Invalid URL provided."));
    }

    [Test]
    public async Task ImportWebPageAsync_WithValidHttpsUrl_DoesNotThrowOnValidation()
    {
        // Arrange
        var url = "https://example.com";
        var tags = new List<string> { "tag1" };
        var expectedDocumentId = "test-doc-id";
        
        _mockKernelMemory.Setup(m => m.ImportWebPageAsync(
            It.IsAny<string>(), 
            It.IsAny<string?>(), 
            It.IsAny<TagCollection?>(), 
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<Microsoft.KernelMemory.Context.IContext?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocumentId);

        // Act - Should not throw validation exception
        var result = await _service.ImportWebPageAsync(url, _testAgentId, tags);
        
        // Assert - Method completes successfully
        Assert.That(result, Is.EqualTo(expectedDocumentId));
    }

    [Test]
    public async Task ImportWebPageAsync_WithValidHttpUrl_DoesNotThrowOnValidation()
    {
        // Arrange
        var url = "http://example.com";
        var tags = new List<string> { "tag1" };
        var expectedDocumentId = "test-doc-id";
        
        _mockKernelMemory.Setup(m => m.ImportWebPageAsync(
            It.IsAny<string>(), 
            It.IsAny<string?>(), 
            It.IsAny<TagCollection?>(), 
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<Microsoft.KernelMemory.Context.IContext?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocumentId);

        // Act - Should not throw validation exception
        var result = await _service.ImportWebPageAsync(url, _testAgentId, tags);
        
        // Assert - Method completes successfully
        Assert.That(result, Is.EqualTo(expectedDocumentId));
    }

    #endregion

    #region ImportTextContentAsync Validation Tests

    [Test]
    public void ImportTextContentAsync_WithNullContent_ThrowsArgumentException()
    {
        // Arrange
        string? content = null;
        var fileName = "test.txt";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportTextContentAsync(content!, fileName, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("content"));
        Assert.That(exception.Message, Does.Contain("Content cannot be null or empty."));
    }

    [Test]
    public void ImportTextContentAsync_WithEmptyContent_ThrowsArgumentException()
    {
        // Arrange
        var content = "";
        var fileName = "test.txt";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportTextContentAsync(content, fileName, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("content"));
        Assert.That(exception.Message, Does.Contain("Content cannot be null or empty."));
    }

    [Test]
    public void ImportTextContentAsync_WithWhitespaceContent_ThrowsArgumentException()
    {
        // Arrange
        var content = "   \t\n   ";
        var fileName = "test.txt";
        var tags = new List<string> { "tag1" };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ImportTextContentAsync(content, fileName, _testAgentId, tags));
        
        Assert.That(exception!.ParamName, Is.EqualTo("content"));
        Assert.That(exception.Message, Does.Contain("Content cannot be null or empty."));
    }

    [Test]
    public async Task ImportTextContentAsync_WithValidContent_DoesNotThrowOnValidation()
    {
        // Arrange
        var content = "This is valid content";
        var fileName = "test.txt";
        var tags = new List<string> { "tag1" };
        var expectedDocumentId = "test-doc-id";
        
        _mockKernelMemory.Setup(m => m.ImportDocumentAsync(
            It.IsAny<Stream>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<TagCollection?>(),
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<Microsoft.KernelMemory.Context.IContext?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocumentId);

        // Act - Should not throw validation exception
        var result = await _service.ImportTextContentAsync(content, fileName, _testAgentId, tags);
        
        // Assert - Method completes successfully
        Assert.That(result, Is.EqualTo(expectedDocumentId));
    }

    #endregion

    #region ImportDocumentAsync Tests

    [Test]
    public async Task ImportDocumentAsync_WithValidStream_ReturnsDocumentId()
    {
        // Arrange
        var content = "Test document content";
        var fileName = "test.txt";
        var tags = new List<string> { "tag1", "tag2" };
        var expectedDocumentId = "test-doc-id";
        
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        
        _mockKernelMemory.Setup(m => m.ImportDocumentAsync(
            It.IsAny<Stream>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<TagCollection?>(),
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<Microsoft.KernelMemory.Context.IContext?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocumentId);

        // Act
        var result = await _service.ImportDocumentAsync(stream, fileName, _testAgentId, tags);

        // Assert
        Assert.That(result, Is.EqualTo(expectedDocumentId));
    }

    [Test]
    public async Task ImportDocumentAsync_WithEmptyTags_ReturnsDocumentId()
    {
        // Arrange
        var content = "Test document content";
        var fileName = "test.txt";
        var tags = new List<string>();
        var expectedDocumentId = "test-doc-id";
        
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        
        _mockKernelMemory.Setup(m => m.ImportDocumentAsync(
            It.IsAny<Stream>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<TagCollection?>(),
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<Microsoft.KernelMemory.Context.IContext?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocumentId);

        // Act
        var result = await _service.ImportDocumentAsync(stream, fileName, _testAgentId, tags);

        // Assert
        Assert.That(result, Is.EqualTo(expectedDocumentId));
    }

    #endregion

    #region RemoveDocumentAsync Tests

    [Test]
    public async Task RemoveDocumentAsync_WithValidDocumentId_CallsDeleteDocument()
    {
        // Arrange
        var documentId = "test-doc-id";
        
        _mockKernelMemory.Setup(m => m.DeleteDocumentAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.RemoveDocumentAsync(documentId, _testAgentId);

        // Assert
        _mockKernelMemory.Verify(m => m.DeleteDocumentAsync(
            documentId,
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RemoveDocumentAsync_WithCancellationToken_PassesCancellationToken()
    {
        // Arrange
        var documentId = "test-doc-id";
        var cancellationToken = new CancellationToken();
        
        _mockKernelMemory.Setup(m => m.DeleteDocumentAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.RemoveDocumentAsync(documentId, _testAgentId, cancellationToken);

        // Assert
        _mockKernelMemory.Verify(m => m.DeleteDocumentAsync(
            documentId,
            It.IsAny<string?>(),
            cancellationToken), Times.Once);
    }

    #endregion

    #region SearchAsync Tests

    [Test]
    public void SearchAsync_WithTags_MethodExists()
    {
        // Arrange
        var query = "test search query";
        var tags = new List<string> { "tag1", "tag2" };
        
        // Act & Assert - Just verify the method can be called (will throw due to mock but that's expected)
        Assert.DoesNotThrowAsync(async () =>
        {
           await _service.SearchAsync(query, _testAgentId, tags);
        });
    }

    [Test]
    public void SearchAsync_WithEmptyTags_MethodExists()
    {
        // Arrange
        var query = "test search query";
        var tags = new List<string>();
        
        // Act & Assert - Just verify the method can be called
        Assert.DoesNotThrowAsync(async () =>
        {
            await _service.SearchAsync(query, _testAgentId, tags);
        });
    }

    [Test]
    public void SearchAsync_WithUserId_MethodExists()
    {
        // Arrange
        var query = "test search query";
        var userId = Guid.NewGuid();
        
        // Act & Assert - Just verify the method can be called
        Assert.DoesNotThrowAsync(async () =>
        {
            await _service.SearchAsync(query, _testAgentId, userId);
        });
    }

    #endregion

    #region ExportDocumentAsync Tests

    [Test]
    public async Task ExportDocumentAsync_WithValidParameters_ReturnsStreamableFileContent()
    {
        // Arrange
        var documentId = "test-doc-id";
        var fileName = "test-file.txt";
        var expectedContent = new StreamableFileContent();
        
        _mockKernelMemory.Setup(m => m.ExportFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContent);

        // Act
        var result = await _service.ExportDocumentAsync(documentId, fileName, _testAgentId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedContent));
    }

    [Test]
    public async Task ExportDocumentAsync_WithCancellationToken_PassesCancellationToken()
    {
        // Arrange
        var documentId = "test-doc-id";
        var fileName = "test-file.txt";
        var cancellationToken = new CancellationToken();
        var expectedContent = new StreamableFileContent();
        
        _mockKernelMemory.Setup(m => m.ExportFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContent);

        // Act
        var result = await _service.ExportDocumentAsync(documentId, fileName, _testAgentId, cancellationToken);

        // Assert
        Assert.That(result, Is.EqualTo(expectedContent));
        _mockKernelMemory.Verify(m => m.ExportFileAsync(
            documentId,
            fileName,
            It.IsAny<string?>(),
            cancellationToken), Times.Once);
    }

    #endregion
}
