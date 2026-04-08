namespace NTG.Agent.Orchestrator.Models.Configuration;

/// <summary>
/// Configuration settings for the Azure Document Intelligence (Form Recognizer) feature.
/// </summary>
public class DocumentIntelligenceSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether Azure Document Intelligence is enabled.
    /// When false, file uploads in the chat are disabled and no OCR processing is performed.
    /// Default is false so the application works without an Azure subscription.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the Azure Document Intelligence service endpoint URL.
    /// Required only when <see cref="IsEnabled"/> is true.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key for the Azure Document Intelligence service.
    /// Required only when <see cref="IsEnabled"/> is true.
    /// </summary>
    public string? ApiKey { get; set; }
}
