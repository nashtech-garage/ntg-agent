using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Data;
using Moq;
using NTG.Agent.MCP.Server.McpTools;
using NTG.Agent.MCP.Server.Services.WebSearch;
using NTG.Agent.Shared.Services.Knowledge;

namespace NTG.Agent.MCP.Server.Tests.McpTools
{
    [TestFixture]
    public class WebSearchToolTests
    {
        private Mock<ITextSearchService> _textSearchServiceMock = null!;
        private Mock<IKnowledgeScraperService> _knowledgeScraperServiceMock = null!;
        private WebSearchTool _webSearchTool = null!;

        [SetUp]
        public void SetUp()
        {
            _textSearchServiceMock = new Mock<ITextSearchService>();
            _knowledgeScraperServiceMock = new Mock<IKnowledgeScraperService>();
            _webSearchTool = new WebSearchTool(_textSearchServiceMock.Object, _knowledgeScraperServiceMock.Object);
        }

        [Test]
        public async Task SearchOnlineAsync_ReturnsSearchResult()
        {
            // Arrange
            var query = "test";
            var conversationId = Guid.NewGuid();
            var top = 2;
            var searchResults = new List<TextSearchResult>
            {
                new TextSearchResult("test1") { Link = "https://a.com" },
                new TextSearchResult("test2") { Link = "https://b.com" }
            };

            _textSearchServiceMock
                .Setup(s => s.SearchAsync(query, top))
                .Returns(searchResults.ToAsyncEnumerable());

            _knowledgeScraperServiceMock
                .Setup(s => s.ImportWebPageAsync(It.IsAny<string>(), conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var expected = new SearchResult();
            _knowledgeScraperServiceMock
                .Setup(s => s.SearchPerConversationAsync(query, conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            // Act
            var result = await _webSearchTool.SearchOnlineAsync(query, conversationId, top);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            _textSearchServiceMock.Verify(s => s.SearchAsync(query, top), Times.Once);
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("https://a.com", conversationId, It.IsAny<CancellationToken>()), Times.Once);
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("https://b.com", conversationId, It.IsAny<CancellationToken>()), Times.Once);
            _knowledgeScraperServiceMock.Verify(s => s.SearchPerConversationAsync(query, conversationId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task SearchOnlineAsync_IgnoresEmptyLinks()
        {
            // Arrange
            var query = "test";
            var conversationId = Guid.NewGuid();
            var searchResults = new List<TextSearchResult>
            {
                new TextSearchResult("test1") {Link=""},
                new TextSearchResult("test2") { Link = null},
                new TextSearchResult("test3") { Link = "https://valid.com" }
            };

            _textSearchServiceMock
                .Setup(s => s.SearchAsync(query, 3))
                .Returns(searchResults.ToAsyncEnumerable());

            _knowledgeScraperServiceMock
                .Setup(s => s.ImportWebPageAsync(It.IsAny<string>(), conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var expected = new SearchResult();
            _knowledgeScraperServiceMock
                .Setup(s => s.SearchPerConversationAsync(query, conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            // Act
            var result = await _webSearchTool.SearchOnlineAsync(query, conversationId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("https://valid.com", conversationId, It.IsAny<CancellationToken>()), Times.Once);
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("", conversationId, It.IsAny<CancellationToken>()), Times.Never);
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync(null, conversationId, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SearchOnlineAsync_ContinuesOnImportFailure()
        {
            // Arrange
            var query = "fail";
            var conversationId = Guid.NewGuid();
            var searchResults = new List<TextSearchResult>
            {
                new TextSearchResult("test1"){Link="https://fail.com"},
                new TextSearchResult("test2"){Link= "https://ok.com" }
            };

            _textSearchServiceMock
                .Setup(s => s.SearchAsync(query, 3))
                .Returns(searchResults.ToAsyncEnumerable());

            _knowledgeScraperServiceMock
                .Setup(s => s.ImportWebPageAsync("https://fail.com", conversationId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("fail"));
            _knowledgeScraperServiceMock
                .Setup(s => s.ImportWebPageAsync("https://ok.com", conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var expected = new SearchResult();
            _knowledgeScraperServiceMock
                .Setup(s => s.SearchPerConversationAsync(query, conversationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            // Act
            var result = await _webSearchTool.SearchOnlineAsync(query, conversationId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("https://fail.com", conversationId, It.IsAny<CancellationToken>()), Times.Once);
            _knowledgeScraperServiceMock.Verify(s => s.ImportWebPageAsync("https://ok.com", conversationId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
