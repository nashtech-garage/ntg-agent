using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats.WebPages;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NTG.Agent.MCP.Server.Services;
using NTG.Agent.MCP.Server.Services.WebSearch;
using NTG.Agent.ServiceDefaults;
using NTG.Agent.Shared.Services.Knowledge;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<MonkeyService>();
builder.Services.AddScoped<ITextSearchService, GoogleTextSearchService>();
builder.Services.AddScoped<IKnowledgeScraperService, KernelMemoryKnowledgeScraper>();
builder.Services.AddScoped<IWebScraper, WebScraper>();

// register GoogleTextSearch as ITextSearch
builder.Services.AddScoped<ITextSearch>(serviceProvider =>
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

builder.Services.AddScoped<IKernelMemory>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var endpoint = configuration["KernelMemory:Endpoint"]
                   ?? throw new InvalidOperationException("KernelMemory:Endpoint configuration is required");
    var apiKey = configuration["KernelMemory:ApiKey"]
                ?? throw new InvalidOperationException("KernelMemory:ApiKey configuration is required");

    return new MemoryWebClient(endpoint, apiKey);
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp();

app.MapDefaultEndpoints();

app.Run();
