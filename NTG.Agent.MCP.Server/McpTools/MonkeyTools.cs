﻿using ModelContextProtocol.Server;
using NTG.Agent.MCP.Server.Services;
using System.ComponentModel;
using System.Text.Json;

namespace NTG.Agent.MCP.Server.McpTools;

[McpServerToolType]
public sealed class MonkeyTools
{
    private readonly MonkeyService monkeyService;

    public MonkeyTools(MonkeyService monkeyService)
    {
        this.monkeyService = monkeyService ?? throw new ArgumentNullException(nameof(monkeyService));
    }

    [McpServerTool, Description("Get a list of monkeys.")]
    public async Task<string> GetMonkeys()
    {
        var monkeys = await monkeyService.GetMonkeys();
        return JsonSerializer.Serialize(monkeys);
    }

    [McpServerTool, Description("Get a monkey by name.")]
    public async Task<string> GetMonkey([Description("The name of the monkey to get details for")] string name)
    {
        var monkey = await monkeyService.GetMonkey(name);
        return JsonSerializer.Serialize(monkey);
    }
}
