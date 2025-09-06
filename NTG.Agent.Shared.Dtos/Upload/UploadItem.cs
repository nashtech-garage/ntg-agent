namespace NTG.Agent.Shared.Dtos.Upload;

public class UploadItem
{
    public string Name { get; set; } = string.Empty;
    public UploadStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Progress { get; set; }
}
