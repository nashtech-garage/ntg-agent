using Microsoft.AspNetCore.Components.Forms;
using NTG.Agent.Shared.Dtos.Upload;

namespace NTG.Agent.WebClient.Client.Dtos;

public class UploadItemClient : UploadItem
{
    public StreamContent? Content { get; set; }
}