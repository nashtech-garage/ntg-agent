namespace NTG.Agent.Common.Dtos.Services;

/// <summary>
/// Centralized service for handling file type operations including MIME type detection,
/// file type categorization, and UI-related file type information.
/// </summary>
public static class FileTypeService
{
    /// <summary>
    /// Gets the MIME content type for a file based on its extension.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The MIME content type string.</returns>
    public static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".xhtml" => "application/xhtml+xml",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".jsonld" => "application/ld+json",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".sh" => "application/x-sh",
            ".csv" => "text/csv",
            ".rtf" => "application/rtf",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            ".epub" => "application/epub+zip",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".tiff" or ".tif" => "image/tiff",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".aac" => "audio/aac",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".oga" => "audio/ogg",
            ".opus" => "audio/opus",
            ".weba" => "audio/webm",
            ".mp4" => "video/mp4",
            ".mpeg" => "video/mpeg",
            ".ogv" => "video/ogg",
            ".ogx" => "application/ogg",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Gets a human-readable file type description based on the file extension.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>A user-friendly file type description.</returns>
    public static string GetFileTypeDescription(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".pdf" => "PDF",
            ".doc" or ".docx" => "Word",
            ".xls" or ".xlsx" => "Excel",
            ".ppt" or ".pptx" => "PowerPoint",
            ".odt" => "OpenDocument Text",
            ".ods" => "OpenDocument Spreadsheet",
            ".odp" => "OpenDocument Presentation",
            ".epub" => "EPUB",
            ".zip" => "ZIP Archive",
            ".rar" => "RAR Archive",
            ".7z" => "7-Zip Archive",
            ".tar" => "TAR Archive",
            ".gz" => "GZIP Archive",
            ".txt" => "Text",
            ".md" => "Markdown",
            ".json" => "JSON",
            ".jsonld" => "JSON-LD",
            ".xml" => "XML",
            ".html" or ".htm" => "HTML",
            ".xhtml" => "XHTML",
            ".js" => "JavaScript",
            ".css" => "CSS",
            ".sh" => "Shell Script",
            ".csv" => "CSV",
            ".rtf" => "RTF",
            ".bmp" => "BMP Image",
            ".gif" => "GIF Image",
            ".jpeg" or ".jpg" => "JPEG Image",
            ".png" => "PNG Image",
            ".tiff" or ".tif" => "TIFF Image",
            ".webp" => "WebP Image",
            ".svg" => "SVG Image",
            ".aac" => "AAC Audio",
            ".mp3" => "MP3 Audio",
            ".wav" => "WAV Audio",
            ".oga" => "OGG Audio",
            ".opus" => "Opus Audio",
            ".weba" => "WebM Audio",
            ".mp4" => "MP4 Video",
            ".mpeg" => "MPEG Video",
            ".ogv" => "OGG Video",
            ".ogx" => "OGG Video",
            ".webm" => "WebM Video",
            _ => "Document"
        };
    }

    /// <summary>
    /// Gets the Bootstrap icon class for a file based on its extension.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The Bootstrap icon class string with color.</returns>
    public static string GetFileIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".pdf" => "bi bi-file-earmark-pdf-fill text-danger",
            ".doc" or ".docx" => "bi bi-file-earmark-word-fill text-primary",
            ".xls" or ".xlsx" => "bi bi-file-earmark-excel-fill text-success",
            ".ppt" or ".pptx" => "bi bi-file-earmark-ppt-fill text-warning",
            ".odt" => "bi bi-file-earmark-word-fill text-info",
            ".ods" => "bi bi-file-earmark-excel-fill text-info",
            ".odp" => "bi bi-file-earmark-ppt-fill text-info",
            ".epub" => "bi bi-file-earmark-text-fill text-purple",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "bi bi-file-earmark-zip-fill text-purple",
            ".txt" or ".rtf" => "bi bi-file-earmark-text-fill text-secondary",
            ".md" => "bi bi-file-earmark-code-fill text-info",
            ".json" or ".jsonld" => "bi bi-file-earmark-code-fill text-warning",
            ".xml" => "bi bi-file-earmark-code-fill text-success",
            ".html" or ".htm" or ".xhtml" => "bi bi-file-earmark-code-fill text-primary",
            ".js" => "bi bi-file-earmark-code-fill text-warning",
            ".css" => "bi bi-file-earmark-code-fill text-info",
            ".sh" => "bi bi-file-earmark-code-fill text-secondary",
            ".csv" => "bi bi-file-earmark-text-fill text-success",
            ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".tiff" or ".tif" or ".webp" or ".svg" => "bi bi-file-earmark-image-fill text-primary",
            ".aac" or ".mp3" or ".wav" or ".oga" or ".opus" or ".weba" => "bi bi-file-earmark-music-fill text-success",
            ".mp4" or ".mpeg" or ".ogv" or ".ogx" or ".webm" => "bi bi-file-earmark-play-fill text-danger",
            _ => "bi bi-file-earmark text-muted"
        };
    }

    /// <summary>
    /// Gets the Bootstrap badge CSS class for a file type.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The Bootstrap badge class string.</returns>
    public static string GetTypeBadgeClass(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".pdf" => "bg-danger",
            ".doc" or ".docx" => "bg-primary",
            ".xls" or ".xlsx" => "bg-success",
            ".ppt" or ".pptx" => "bg-warning",
            ".odt" or ".ods" or ".odp" => "bg-info",
            ".epub" => "bg-purple",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "bg-dark",
            ".txt" or ".rtf" => "bg-secondary",
            ".md" => "bg-info",
            ".json" or ".jsonld" => "bg-warning",
            ".xml" => "bg-success",
            ".html" or ".htm" or ".xhtml" => "bg-primary",
            ".js" => "bg-warning",
            ".css" => "bg-info",
            ".sh" => "bg-secondary",
            ".csv" => "bg-success",
            ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".tiff" or ".tif" or ".webp" or ".svg" => "bg-primary",
            ".aac" or ".mp3" or ".wav" or ".oga" or ".opus" or ".weba" => "bg-success",
            ".mp4" or ".mpeg" or ".ogv" or ".ogx" or ".webm" => "bg-danger",
            _ => "bg-light text-dark"
        };
    }

    /// <summary>
    /// Gets the binary file type icon suffix for Bootstrap icons.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The icon suffix string.</returns>
    public static string GetBinaryFileTypeIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".pdf" => "pdf-fill",
            ".doc" or ".docx" => "word-fill",
            ".xls" or ".xlsx" => "excel-fill",
            ".ppt" or ".pptx" => "powerpoint-fill",
            ".odt" => "word-fill",
            ".ods" => "excel-fill",
            ".odp" => "powerpoint-fill",
            ".epub" => "book-fill",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "zip-fill",
            ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".tiff" or ".tif" or ".webp" or ".svg" => "image-fill",
            ".aac" or ".mp3" or ".wav" or ".oga" or ".opus" or ".weba" => "music-note-fill",
            ".mp4" or ".mpeg" or ".ogv" or ".ogx" or ".webm" => "play-fill",
            _ => "fill"
        };
    }

    /// <summary>
    /// Determines the document view type based on file extension.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The document view type.</returns>
    public static DocumentViewType GetDocumentViewType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";

        var extensionType = extension switch
        {
            ".txt" or ".md" or ".js" or ".css" or ".sh" or ".csv" or ".rtf" => DocumentViewType.Text,
            ".json" or ".jsonld" => DocumentViewType.Json,
            ".html" or ".htm" or ".xhtml" => DocumentViewType.Html,
            ".xml" => DocumentViewType.Xml,
            ".pdf" or ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx" or ".odt" or ".ods" or ".odp" or ".epub" or ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => DocumentViewType.Binary,
            ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".tiff" or ".tif" or ".webp" or ".svg" => DocumentViewType.Binary,
            ".aac" or ".mp3" or ".wav" or ".oga" or ".opus" or ".weba" => DocumentViewType.Binary,
            ".mp4" or ".mpeg" or ".ogv" or ".ogx" or ".webm" => DocumentViewType.Binary,
            _ => (DocumentViewType?)null
        };

        // If we have a valid extension type, use it
        if (extensionType.HasValue)
        {
            return extensionType.Value;
        }

        // If no extension match and it's a URL, treat as web page
        if (fileName.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase) || fileName.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase))
        {
            return DocumentViewType.WebPage;
        }

        // Default to binary for unknown/unlisted file types
        return DocumentViewType.Binary;
    }

    /// <summary>
    /// Gets syntax highlighting information for text files.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <param name="documentId">The document ID for creating unique element IDs.</param>
    /// <returns>A tuple containing the language class and element ID.</returns>
    public static (string languageClass, string elementId) GetTextLanguageInfo(string fileName, string documentId)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".md" => ("language-markdown", $"markdown-content-{documentId}"),
            ".js" => ("language-javascript", $"js-content-{documentId}"),
            ".css" => ("language-css", $"css-content-{documentId}"),
            ".sh" => ("language-bash", $"shell-content-{documentId}"),
            ".csv" => ("language-csv", $"csv-content-{documentId}"),
            ".rtf" or ".txt" => ("", ""), // RTF, TXT do not have good syntax highlighting support
            _ => ("", "")
        };
    }

    /// <summary>
    /// Gets the data-language attribute value for CSS styling.
    /// </summary>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <returns>The data-language attribute value.</returns>
    public static string GetDataLanguageAttribute(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return extension switch
        {
            ".md" => "markdown",
            ".js" => "javascript",
            ".css" => "css",
            ".sh" => "bash",
            ".csv" => "csv",
            _ => ""
        };
    }

    /// <summary>
    /// Checks if a content type is suitable for text preview.
    /// </summary>
    /// <param name="contentType">The MIME content type to check.</param>
    /// <returns>True if the content type is text-based, false otherwise.</returns>
    public static bool IsTextBasedContentType(string contentType)
    {
        var textTypes = new[]
        {
            "text/plain",
            "text/markdown",
            "text/x-markdown",
            "text/plain-markdown",
            "text/html",
            "application/xhtml+xml",
            "application/xml",
            "text/xml",
            "application/ld+json",
            "text/css",
            "text/javascript",
            "application/x-sh",
            "text/x-uri",
            "application/json",
            "text/csv",
            "application/rtf"
        };

        return textTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all supported file extensions for file input accept attribute.
    /// </summary>
    /// <returns>A comma-separated string of all supported file extensions.</returns>
    public static string GetSupportedFileExtensions()
    {
        return ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.epub,.zip,.rar,.7z,.tar,.gz,.txt,.md,.csv,.rtf,.html,.htm,.xhtml,.json,.jsonld,.xml,.js,.css,.sh,.bmp,.gif,.jpeg,.jpg,.png,.tiff,.tif,.webp,.svg,.aac,.mp3,.wav,.oga,.opus,.weba,.mp4,.mpeg,.ogv,.ogx,.webm";
    }

    /// <summary>
    /// Gets a user-friendly description of all supported file formats.
    /// </summary>
    /// <returns>A descriptive string of supported formats.</returns>
    public static string GetSupportedFormatsDescription()
    {
        return "Supported formats: PDF, Word (.doc/.docx), Excel (.xls/.xlsx), PowerPoint (.ppt/.pptx), OpenDocument (.odt/.ods/.odp), EPUB, Archives (.zip/.rar/.7z/.tar/.gz), Text (.txt/.md/.csv/.rtf), Web (.html/.htm/.xhtml), Data (.json/.jsonld/.xml), Code (.js/.css/.sh), Images, Audio, Video (Max 50MB each)";
    }

    /// <summary>
    /// Gets all supported file extensions for Azure AI Document Intelligence file input accept attribute.
    /// </summary>
    /// <returns>A comma-separated string of all supported file extensions.</returns>
    public static string GetSupportedDocumentFileExtensions()
    {
        return ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.epub,.zip,.rar,.7z,.tar,.gz,.txt,.md,.csv,.rtf,.html,.htm,.xhtml,.json,.jsonld,.xml,.js,.css,.sh,.bmp,.gif,.jpeg,.jpg,.png,.tiff,.tif,.webp,.svg,.aac,.mp3,.wav,.oga,.opus,.weba,.mp4,.mpeg,.ogv,.ogx,.webm";
    }

    /// <summary>
    /// Gets a user-friendly description of all Azure AI Document Intelligence supported file formats.
    /// </summary>
    /// <returns>A descriptive string of supported formats.</returns>
    public static string GetSupportedDocumentFormatsDescription()
    {
        return "Supported formats: PDF, Word (.doc/.docx), Excel (.xls/.xlsx), PowerPoint (.ppt/.pptx), OpenDocument (.odt/.ods/.odp), EPUB, Archives (.zip/.rar/.7z/.tar/.gz), Text (.txt/.md/.csv/.rtf), Web (.html/.htm/.xhtml), Data (.json/.jsonld/.xml), Code (.js/.css/.sh), Images, Audio, Video (Max 50MB each)";
    }

    /// <summary>
    /// Gets the file extension from a content type for URL downloads.
    /// </summary>
    /// <param name="contentType">The MIME content type.</param>
    /// <param name="url">The URL as fallback for extension detection.</param>
    /// <returns>The appropriate file extension.</returns>
    public static string GetFileExtensionFromContentType(string contentType, string url)
    {
        try
        {
            var urlExt = Path.GetExtension(new Uri(url).AbsolutePath);
            if (!string.IsNullOrEmpty(urlExt)) return urlExt;
        }
        catch { /* ignore */ }

        return (contentType ?? string.Empty).ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/html" => ".html",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "application/zip" => ".zip",
            _ => ".html"
        };
    }

    /// <summary>
    /// Sanitizes a filename for safe use in file operations.
    /// </summary>
    /// <param name="fileName">The filename to sanitize.</param>
    /// <returns>A sanitized filename safe for file operations.</returns>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "download";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());

        sanitized = sanitized
            .Replace("://", "_")
            .Replace("/", "_")
            .Replace("?", "_")
            .Replace("&", "_")
            .Replace("=", "_")
            .Replace("#", "_");

        if (sanitized.Length > 120) sanitized = sanitized[..120];
        return string.IsNullOrWhiteSpace(sanitized) ? "download" : sanitized;
    }
}

/// <summary>
/// Enumeration of different document view types for the document viewer.
/// </summary>
public enum DocumentViewType
{
    Text,
    Json,
    Html,
    Xml,
    WebPage,
    Binary
}
