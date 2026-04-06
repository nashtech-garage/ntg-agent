using NTG.Agent.Common.Dtos.Upload;

namespace NTG.Agent.WebClient.Client.Dtos;

public class UploadItemClient : UploadItem
{
    /// <summary>File size in bytes, sourced from <see cref="Microsoft.AspNetCore.Components.Forms.IBrowserFile.Size"/>.</summary>
    public long Size { get; set; }

    public StreamContent? Content { get; set; }
}