# NTG Agent - Search Online Tool

## Project Summary

The **NTG Agent Search Online Tool** is an AI tool library that provides web search capabilities through Google Custom Search Engine integration. Built on .NET 10 and implementing the Model Context Protocol (MCP), this tool enables AI agents to search the web, retrieve relevant results, and scrape webpage content for context-aware responses.

The tool integrates with both the MCP Server and can be used directly by AI agents to augment their knowledge with real-time web information.

## Project Structure

```
NTG.Agent.AITools.SearchOnlineTool/
??? Services/
?   ??? ITextSearchService.cs            # Text search abstraction
?   ??? GoogleTextSearchService.cs       # Google search implementation
?   ??? IWebScraper.cs                   # Web scraping interface
?   ??? WebScraper.cs                    # HTML content extraction
??? Dtos/
?   ??? WebSearchResult.cs               # Search result model
?   ??? WebScraperResult.cs              # Scraping result model
??? Extensions/
?   ??? AiToolRegistrationExtensions.cs  # DI registration
?   ??? HtmlExtensions.cs                # HTML cleaning utilities
?   ??? StreamExtensions.cs              # Stream helpers
??? SearchOnlineTool.cs                  # MCP tool implementation
??? NTG.Agent.AITools.SearchOnlineTool.csproj
```

## Main Components

### MCP Tool

**SearchOnlineTool** - Exposes web search to AI agents:
```csharp
[McpServerTool, Description("Search Online Web")]
public async Task<string> SearchOnlineAsync(
    [Description("the value to search")] string query,
    [Description("Maximum number of online search results to fetch")] int top = 3)
{
    // 1. Get search results from Google
    var searchResults = await _textSearchService.SearchAsync(query, top);
    
    // 2. Scrape webpage content
    var webPages = await ScrapeWebPagesAsync(searchResults);
    
    // 3. Return cleaned content
    return JsonSerializer.Serialize(webPages);
}
```

### Services

**GoogleTextSearchService** - Google Custom Search integration:
- Connects to Google Custom Search API
- Searches indexed web pages
- Returns URLs and snippets
- Configurable result count

**WebScraper** - HTML content extraction:
- Fetches web pages via HTTP
- Extracts text content
- Cleans HTML markup
- Handles various content types
- Retry logic for transient failures

### DTOs

**WebSearchResult**
```csharp
public class WebSearchResult
{
    public string Url { get; set; }
    public string Content { get; set; }  // Cleaned HTML content
}
```

**WebScraperResult**
```csharp
public class WebScraperResult
{
    public bool Success { get; set; }
    public BinaryData? Content { get; set; }
    public string? ContentType { get; set; }
    public string? Error { get; set; }
}
```

### Extensions

**HTML Cleaning**
```csharp
public static string CleanHtml(this string html)
{
    // Removes scripts, styles, comments
    // Extracts readable text
    // Preserves structure
}
```

## Design Patterns Used

1. **Strategy Pattern** - Pluggable search service implementations
2. **Adapter Pattern** - Adapts Google search to ITextSearchService
3. **Facade Pattern** - Simplifies web scraping complexity
4. **Factory Pattern** - HTTP client creation
5. **Template Method Pattern** - Web scraping workflow

## Features

### Web Search

- **Google Custom Search** - Leverages Google's search index
- **Configurable Results** - Control number of results returned
- **Parallel Processing** - Fetches web pages concurrently
- **Error Resilience** - Continues on individual page failures

### Web Scraping

- **Content Extraction** - Extracts meaningful text from HTML
- **HTML Cleaning** - Removes scripts, styles, and markup
- **Content Type Detection** - Handles different MIME types
- **Retry Logic** - Handles transient failures (408, 500, 502, 504)
- **Timeout Protection** - Prevents hanging requests

### MCP Integration

- **Tool Discovery** - Agents can find the search tool
- **Parameter Validation** - Typed parameters with descriptions
- **JSON Serialization** - Structured results for AI consumption

## Dependencies

### NuGet Packages

- **ModelContextProtocol.AspNetCore** (0.4.0-preview.3) - MCP server support
- **Microsoft.SemanticKernel.Plugins.Web** (1.67.1-alpha) - Google search integration
- **System.Memory.Data** (10.0.0) - Binary data handling

### Project References

- **NTG.Agent.ServiceDefaults** - Shared configuration and logging

## How to Setup

### Prerequisites

- .NET 10 SDK
- Google Custom Search Engine (CSE) credentials

### Google Custom Search Engine Setup

1. **Create a Custom Search Engine**

Visit: https://programmablesearchengine.google.com/

- Click "Add"
- Name your search engine
- **Sites to search**: Select "Search the entire web"
- Click "Create"

2. **Get Search Engine ID**

- In your search engine settings
- Copy the **Search engine ID** (looks like: `012345678901234567890:abcd1234`)

3. **Get API Key**

Visit: https://console.cloud.google.com/apis/credentials

- Create a new project (or select existing)
- Enable "Custom Search API"
- Create credentials ? API Key
- Copy the API key

### Configuration

Set credentials via user secrets:

```bash
cd NTG.Agent.MCP.Server  # Or any project using this tool
dotnet user-secrets set "Google:ApiKey" "your-google-api-key"
dotnet user-secrets set "Google:SearchEngineId" "your-search-engine-id"
```

Or in `appsettings.json` (not recommended for production):
```json
{
  "Google": {
    "ApiKey": "your-google-api-key",
    "SearchEngineId": "your-search-engine-id"
  }
}
```

### Registration in MCP Server

In the MCP Server `Program.cs`:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .AddAiTool();  // Registers SearchOnlineTool
```

The `.AddAiTool()` extension:
- Registers `SearchOnlineTool` as MCP tool
- Configures `GoogleTextSearchService`
- Configures `WebScraper`
- Sets up Google CSE connection

## Usage

### From AI Agents

When configured, agents can use the search tool:

**User:** "What are the latest developments in AI?"

**Agent:**
1. Detects need for web search
2. Calls `SearchOnlineAsync("latest AI developments")`
3. Receives web content from top 3 results
4. Synthesizes information
5. Responds with current information

### Direct Usage

```csharp
public class MyService
{
    private readonly ITextSearchService _searchService;
    private readonly IWebScraper _webScraper;

    public async Task<List<WebSearchResult>> SearchWebAsync(string query)
    {
        var results = new List<WebSearchResult>();
        
        // 1. Search
        await foreach (var result in _searchService.SearchAsync(query, top: 5))
        {
            // 2. Scrape content
            var webPage = await _webScraper.GetContentAsync(result.Link);
            
            // 3. Clean HTML
            var cleanContent = webPage.Content.ToString().CleanHtml();
            
            results.Add(new WebSearchResult
            {
                Url = result.Link,
                Content = cleanContent
            });
        }
        
        return results;
    }
}
```

## API Reference

### ITextSearchService

```csharp
public interface ITextSearchService
{
    IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top);
}
```

**TextSearchResult** (from Semantic Kernel):
- `Name` - Page title
- `Link` - URL
- `Value` - Snippet/description

### IWebScraper

```csharp
public interface IWebScraper
{
    Task<WebScraperResult> GetContentAsync(string url, CancellationToken cancellationToken = default);
}
```

**Parameters:**
- `url` - Web page URL (must be HTTP/HTTPS)
- `cancellationToken` - Cancellation support

**Returns:**
- `Success` - Whether scraping succeeded
- `Content` - Binary content data
- `ContentType` - MIME type
- `Error` - Error message (if failed)

### SearchOnlineTool

```csharp
[McpServerTool, Description("Search Online Web")]
public async Task<string> SearchOnlineAsync(string query, int top = 3)
```

**Parameters:**
- `query` - Search query string
- `top` - Maximum number of results (default: 3)

**Returns:**
- JSON-serialized array of `WebSearchResult`

## Configuration Options

### Search Results Limit

```csharp
// In tool invocation
var results = await SearchOnlineAsync("AI news", top: 5);  // Get 5 results
```

### HTTP Client Timeout

```csharp
builder.Services.AddHttpClient()
    .ConfigureHttpClientDefaults(http =>
    {
        http.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    });
```

### Retry Policy

Built-in retry for:
- **408** - Request Timeout
- **500** - Internal Server Error
- **502** - Bad Gateway
- **504** - Gateway Timeout

Retry delays: 1s, 1s, 1s, 2s, 2s, 3s, 4s, 5s, 5s, 5s (max 10 attempts)

## Performance Considerations

### Parallel Web Scraping

The tool scrapes web pages in parallel:

```csharp
var importTasks = searchResults
    .Select(async result => await _webScraper.GetContentAsync(result.Link));

await Task.WhenAll(importTasks);  // Parallel execution
```

### Error Handling

Individual page failures don't stop the process:

```csharp
try
{
    var webPage = await _webScraper.GetContentAsync(url);
    results.Add(webPage);
}
catch
{
    // Ignore failures, continue with other pages
}
```

### Content Cleaning

HTML cleaning is optimized for:
- Removing non-essential elements (scripts, styles)
- Extracting readable text
- Preserving semantic structure
- Minimizing token usage for AI

## Limitations

### Google CSE Limits

**Free Tier:**
- 100 queries per day
- Max 10 results per query

**Paid Tier:**
- Up to 10,000 queries per day
- Higher rate limits

### Content Types

Works best with:
- ? HTML pages
- ? Plain text
- ? Markdown

Limited support for:
- ?? JavaScript-heavy SPAs
- ?? Pages requiring authentication
- ?? Dynamic content

Not supported:
- ? PDF files
- ? Binary documents
- ? Media files

## Troubleshooting

### "Google:ApiKey is missing"

Ensure configuration is set:
```bash
dotnet user-secrets set "Google:ApiKey" "your-key"
```

### "Google:SearchEngineId is missing"

Set search engine ID:
```bash
dotnet user-secrets set "Google:SearchEngineId" "your-id"
```

### Search returns no results

1. Check Google CSE is configured for "entire web"
2. Verify API key has Custom Search API enabled
3. Check daily quota hasn't been exceeded

### Web scraping fails

1. Check URL is valid HTTP/HTTPS
2. Verify network connectivity
3. Some sites block automated access (robots.txt, rate limiting)

## Testing

### Unit Testing

```csharp
[Test]
public async Task SearchOnlineAsync_ValidQuery_ReturnsResults()
{
    // Arrange
    var mockSearchService = new Mock<ITextSearchService>();
    var mockWebScraper = new Mock<IWebScraper>();
    var tool = new SearchOnlineTool(mockSearchService.Object, mockWebScraper.Object);
    
    // Act
    var results = await tool.SearchOnlineAsync("test query", 3);
    
    // Assert
    Assert.That(results, Is.Not.Empty);
}
```

### Integration Testing

```csharp
[Test]
public async Task GoogleTextSearchService_RealQuery_ReturnsResults()
{
    // Requires real Google CSE credentials
    var service = new GoogleTextSearchService(configuredGoogleSearch);
    
    var results = new List<TextSearchResult>();
    await foreach (var result in service.SearchAsync("C# programming", 3))
    {
        results.Add(result);
    }
    
    Assert.That(results, Has.Count.GreaterThan(0));
}
```

## Future Enhancements

- [ ] Support for more search engines (Bing, DuckDuckGo)
- [ ] Caching of search results
- [ ] Advanced HTML-to-text conversion
- [ ] PDF content extraction
- [ ] Image search support
- [ ] News-specific search
- [ ] Custom result ranking

## Additional Resources

- [Main Solution README](../../README.md)
- [Google Custom Search API](https://developers.google.com/custom-search/v1/overview)
- [Model Context Protocol](https://modelcontextprotocol.io)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)
