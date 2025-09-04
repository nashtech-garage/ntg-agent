namespace NTG.Agent.Shared.Dtos.Upload;

public class UploadItemContent : UploadItem
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
