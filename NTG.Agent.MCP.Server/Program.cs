using NTG.Agent.AITools.SearchOnlineTool.Extensions;
using NTG.Agent.MCP.Server.Services;
using NTG.Agent.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<MonkeyService>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .AddAiTool();

var app = builder.Build();

app.MapMcp();

app.MapDefaultEndpoints();

app.Run();
