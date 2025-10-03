using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats.WebPages;
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
builder.Services.AddSingleton<IWebScraper, WebScraper>();

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
