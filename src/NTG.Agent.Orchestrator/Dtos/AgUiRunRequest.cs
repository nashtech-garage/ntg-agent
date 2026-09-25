using System.Text.Json;
using System.Text.Json.Serialization;

namespace NTG.Agent.Orchestrator.Dtos;

public class AgUiRunRequest
{
    public string ThreadId { get; set; } = "";
    public string RunId { get; set; } = "";
    public List<AgUiMessage> Messages { get; set; } = [];
    public List<AgUiTool>? Tools { get; set; }
}

public class AgUiMessage
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    // user / system / developer messages
    public string? Content { get; set; }
    // tool result message
    public string? ToolCallId { get; set; }
    // assistant message with tool calls
    public List<AgUiToolCall>? ToolCalls { get; set; }
}

public class AgUiToolCall
{
    public string Id { get; set; } = "";
    public AgUiFunction? Function { get; set; }
}

public class AgUiFunction
{
    public string Name { get; set; } = "";
    public string? Arguments { get; set; }
}

public class AgUiTool
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }
}
