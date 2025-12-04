# NTG Agent - Common Library

## Project Summary

The **NTG Agent Common** library is a shared class library that provides reusable Data Transfer Objects (DTOs), constants, enums, and utility services used across all projects in the NTG Agent solution. By centralizing these shared components, the library promotes consistency, reduces code duplication, and ensures that data contracts remain synchronized across client and server boundaries.

This project follows the principle of DRY (Don't Repeat Yourself) and serves as the single source of truth for shared data models and business constants.

## Project Structure

```
NTG.Agent.Common/
??? Dtos/
?   ??? Agents/                          # Agent-related DTOs
?   ?   ??? AgentListItemDto.cs
?   ?   ??? AgentDetail.cs
?   ?   ??? AgentToolDto.cs
?   ?   ??? AgentToolType.cs
?   ??? Chats/                           # Chat and conversation DTOs
?   ?   ??? PromptRequest.cs
?   ?   ??? PromptResponse.cs
?   ?   ??? ConversationDto.cs
?   ??? Documents/                       # Document DTOs
?   ?   ??? DocumentListItem.cs
?   ?   ??? DocumentViewType.cs
?   ?   ??? UploadItemClient.cs
?   ??? SharedConversations/             # Conversation sharing DTOs
?   ??? Services/                        # Service utilities
?   ?   ??? FileTypeService.cs           # File type detection
?   ??? Constants/
?       ??? Constants.cs                 # Shared constants
??? Logger/                              # Logging extensions
??? NTG.Agent.Common.csproj
```

## Main Components

### DTOs (Data Transfer Objects)

#### Agent DTOs

**AgentListItemDto**
```csharp
public record AgentListItemDto(
    Guid Id,
    string Name,
    string Instructions,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
```

**AgentDetail** - Complete agent configuration including:
- Basic info (name, instructions)
- LLM provider settings (endpoint, API key, model)
- MCP server configuration
- Tool associations

**AgentToolDto**
- Tool name and description
- Tool type (Built-in or MCP)
- Enabled status
- MCP server endpoint

**AgentToolType** - Enum
```csharp
public enum AgentToolType
{
    BuiltIn = 1,
    MCP = 2
}
```

#### Chat DTOs

**PromptRequest<T>**
```csharp
public class PromptRequest<T>
{
    public string Prompt { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AgentId { get; set; }
    public string? SessionId { get; set; }
    public IEnumerable<T>? Documents { get; set; }
}
```

**PromptResponse**
```csharp
public record PromptResponse(
    string Text,
    string? ToolName,
    string? ToolResult
);
```

#### Document DTOs

**DocumentListItem**
```csharp
public record DocumentListItem(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<string> Tags
);
```

**DocumentViewType** - Enum for document preview
```csharp
public enum DocumentViewType
{
    Text,
    Json,
    Html,
    Xml,
    WebPage,
    Binary
}
```

### Services

#### FileTypeService

Centralized file type operations including:
- MIME type detection
- File type categorization
- UI icon selection
- Document preview support
- Syntax highlighting info

**Key Methods:**

```csharp
// Get MIME type
string contentType = FileTypeService.GetContentType("document.pdf");
// Returns: "application/pdf"

// Get user-friendly description
string description = FileTypeService.GetFileTypeDescription("document.pdf");
// Returns: "PDF"

// Get Bootstrap icon class
string icon = FileTypeService.GetFileIcon("document.pdf");
// Returns: "bi bi-file-earmark-pdf-fill text-danger"

// Get document view type
DocumentViewType viewType = FileTypeService.GetDocumentViewType("document.txt");
// Returns: DocumentViewType.Text

// Check if text-based
bool isText = FileTypeService.IsTextBasedContentType("text/plain");
// Returns: true

// Sanitize filename
string safe = FileTypeService.SanitizeFileName("my file?.pdf");
// Returns: "my file_.pdf"
```

**Supported Formats:**
- Documents: PDF, Word, Excel, PowerPoint, OpenDocument, EPUB
- Text: Plain text, Markdown, JSON, XML, HTML, CSV, RTF
- Code: JavaScript, CSS, Shell scripts
- Archives: ZIP, RAR, 7z, tar, gz
- Images: JPEG, PNG, GIF, BMP, TIFF, WebP, SVG
- Audio: AAC, MP3, WAV, OGG, Opus, WebM Audio
- Video: MP4, MPEG, OGG Video, WebM Video

### Constants

**Constants.cs** - Shared application constants:
```csharp
public static class Constants
{
    public const string AnonymousRoleId = "3dc04c42-9b42-4920-b7f2-29dfc2c5d169";
    public const string AdminRoleId = "d5147680-87f5-41dc-aff2-e041959c2fa1";
    // ... other constants
}
```

### Logger Extensions

**Business Event Logging:**
```csharp
_logger.LogBusinessEvent("DocumentsRetrieved", new 
{ 
    AgentId = agentId, 
    DocumentCount = documents.Count 
});
```

## Design Patterns Used

1. **DTO Pattern** - Separates domain models from data transfer
2. **Record Types** - Immutable DTOs for thread safety
3. **Service Locator Pattern** - FileTypeService as utility class
4. **Strategy Pattern** - Different file type handling strategies
5. **Facade Pattern** - FileTypeService hides complex file operations

## Features

### Type Safety

- Strong typing for all data contracts
- Compile-time validation
- IntelliSense support
- Refactoring safety

### Consistency

- Single source of truth for DTOs
- Shared across all projects
- Synchronized client/server contracts
- Prevents data model drift

### File Type Intelligence

The FileTypeService provides comprehensive file handling:

**MIME Type Detection**
```csharp
.txt ? text/plain
.pdf ? application/pdf
.docx ? application/vnd.openxmlformats-officedocument.wordprocessingml.document
.jpg ? image/jpeg
```

**UI Support**
- Bootstrap icon classes
- Badge colors
- File type descriptions
- Preview capabilities

**Syntax Highlighting**
```csharp
var (languageClass, elementId) = FileTypeService.GetTextLanguageInfo("script.js", documentId);
// Returns: ("language-javascript", "js-content-{documentId}")
```

### Extensibility

Easy to extend with new types:

```csharp
// In FileTypeService.cs
public static string GetContentType(string fileName)
{
    var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
    return extension switch
    {
        // ... existing cases
        ".mynewtype" => "application/x-mynewtype",
        _ => "application/octet-stream"
    };
}
```

## Dependencies

### NuGet Packages

- None (pure .NET 10 class library)
- Uses only built-in .NET namespaces
- No external dependencies

### Referenced By

- NTG.Agent.Admin
- NTG.Agent.Admin.Client
- NTG.Agent.WebClient
- NTG.Agent.WebClient.Client
- NTG.Agent.Orchestrator

## How to Use

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\NTG.Agent.Common\NTG.Agent.Common.csproj" />
</ItemGroup>
```

### 2. Import Namespaces

```csharp
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Documents;
using NTG.Agent.Common.Dtos.Services;
```

### 3. Use DTOs

```csharp
// Creating a prompt request
var request = new PromptRequest<UploadItemClient>
{
    Prompt = "Tell me about this document",
    ConversationId = Guid.NewGuid(),
    AgentId = selectedAgentId,
    Documents = uploadedFiles
};

// Using file type service
var contentType = FileTypeService.GetContentType(fileName);
var icon = FileTypeService.GetFileIcon(fileName);
var viewType = FileTypeService.GetDocumentViewType(fileName);
```

## Development

### Adding New DTOs

1. **Create the DTO class**

```csharp
// In Dtos/MyFeature/MyDto.cs
namespace NTG.Agent.Common.Dtos.MyFeature;

public record MyDto(
    Guid Id,
    string Name,
    DateTime CreatedAt
);
```

2. **Use across projects**

```csharp
// In any project that references Common
using NTG.Agent.Common.Dtos.MyFeature;

public async Task<MyDto> GetData() { ... }
```

### Adding File Types

1. **Update FileTypeService.GetContentType()**

```csharp
".newext" => "application/x-newtype",
```

2. **Update GetFileTypeDescription()**

```csharp
".newext" => "New Type Description",
```

3. **Update GetFileIcon()**

```csharp
".newext" => "bi bi-file-earmark text-primary",
```

### Adding Constants

```csharp
// In Constants/Constants.cs
public static class Constants
{
    public const string MyNewConstant = "value";
}
```

## Best Practices

### 1. Use Records for DTOs

```csharp
// ? Good - Immutable, concise
public record UserDto(Guid Id, string Name);

// ? Avoid - Mutable, verbose
public class UserDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
```

### 2. Keep DTOs Simple

```csharp
// ? Good - Data only, no logic
public record ProductDto(Guid Id, string Name, decimal Price);

// ? Avoid - Business logic in DTO
public record ProductDto(Guid Id, string Name, decimal Price)
{
    public decimal GetDiscountedPrice() => Price * 0.9m; // NO!
}
```

### 3. Version DTOs

When making breaking changes:

```csharp
// V1
public record UserDto(Guid Id, string Name);

// V2 - Add new optional property
public record UserDto(Guid Id, string Name, string? Email = null);

// Or create new version
public record UserDtoV2(Guid Id, string Name, string Email);
```

### 4. Namespace Organization

```csharp
// Organize by feature
NTG.Agent.Common.Dtos.Agents
NTG.Agent.Common.Dtos.Chats
NTG.Agent.Common.Dtos.Documents
NTG.Agent.Common.Dtos.Folders
```

## Testing

### Unit Testing DTOs

```csharp
[Test]
public void DocumentListItem_Should_Create_With_AllProperties()
{
    var doc = new DocumentListItem(
        Guid.NewGuid(),
        "test.pdf",
        DateTime.UtcNow,
        DateTime.UtcNow,
        new List<string> { "tag1", "tag2" }
    );
    
    Assert.That(doc.Name, Is.EqualTo("test.pdf"));
    Assert.That(doc.Tags, Has.Count.EqualTo(2));
}
```

### Testing FileTypeService

```csharp
[Test]
public void GetContentType_PdfFile_ReturnsCorrectMimeType()
{
    var contentType = FileTypeService.GetContentType("document.pdf");
    Assert.That(contentType, Is.EqualTo("application/pdf"));
}

[Test]
public void SanitizeFileName_InvalidChars_RemovesInvalidChars()
{
    var safe = FileTypeService.SanitizeFileName("file?name<test>.pdf");
    Assert.That(safe, Does.Not.Contain("?"));
    Assert.That(safe, Does.Not.Contain("<"));
    Assert.That(safe, Does.Not.Contain(">"));
}
```

## Versioning

When updating DTOs that are used in APIs:

1. **Additive Changes** (Safe)
   - Add optional properties
   - Add new DTOs
   - Backward compatible

2. **Breaking Changes** (Careful!)
   - Remove properties
   - Rename properties
   - Change types
   - Requires API versioning

## Additional Resources

- [Main Solution README](../README.md)
- [C# Records](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record)
- [DTO Pattern](https://martinfowler.com/eaaCatalog/dataTransferObject.html)
- [API Versioning](https://learn.microsoft.com/aspnet/core/web-api/advanced/versioning)
