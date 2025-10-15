using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NTG.Agent.MCP.Server.Services;
using NTG.Agent.MCP.Server.Services.WebSearch;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<MonkeyService>();
builder.Services.AddSingleton<ITextSearchService, GoogleTextSearchService>();
builder.Services.AddSingleton<IWebScraper, WebScraper>();

// register GoogleTextSearch as ITextSearch
builder.Services.AddSingleton<ITextSearch>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();

    // read settings
    var apiKey = configuration["Google:ApiKey"]
        ?? throw new InvalidOperationException("Google:ApiKey is missing.");
    var cseId = configuration["Google:SearchEngineId"]
        ?? throw new InvalidOperationException("Google:SearchEngineId is missing.");
#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    return new GoogleTextSearch(
        initializer: new() { ApiKey = apiKey },
        searchEngineId: cseId);
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp();

app.MapDefaultEndpoints();

app.Run();
