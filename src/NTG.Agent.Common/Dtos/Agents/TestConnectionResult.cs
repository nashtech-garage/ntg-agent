namespace NTG.Agent.Common.Dtos.Agents;

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int ModelCount { get; set; }
    public List<ModelItem> Models { get; set; } = new();
}
