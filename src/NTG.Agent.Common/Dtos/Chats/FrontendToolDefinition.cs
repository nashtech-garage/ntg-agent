using System.Text.Json;

namespace NTG.Agent.Common.Dtos.Chats;

/// <summary>
/// Framework-neutral metadata for a tool that is declared to the model but executed by the client.
/// </summary>
public sealed record FrontendToolDefinition(
    string Name,
    string? Description,
    JsonElement Parameters);
