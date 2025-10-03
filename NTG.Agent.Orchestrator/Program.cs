using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Knowledge;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.ServiceDefaults;
using NTG.Agent.Shared.Services.Knowledge;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// Endpoint to the Aspire Dashboard
var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? throw new Exception("OTEL_EXPORTER_OTLP_ENDPOINT configuration key is required but not found");

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService("TelemetryAspireDashboardQuickstart");

// Enable model diagnostics with sensitive data.
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource("Microsoft.SemanticKernel*")
    .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint))
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter("Microsoft.SemanticKernel*")
    .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint))
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    // Add OpenTelemetry as a logging provider
    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
        // Format log messages. This is default to false.
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    });
    builder.SetMinimumLevel(LogLevel.Debug);
});

builder.AddServiceDefaults();

builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("../key/"))
    .SetApplicationName("NTGAgent");

await using IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new SseClientTransport(new()
    {
        Name = "ntgmcpserver",
        Endpoint = new Uri("http://localhost:5136")
    })
);

var tools = await mcpClient.ListToolsAsync();

builder.Services.AddSingleton<Kernel>(serviceBuilder =>
{
    var config = serviceBuilder.GetRequiredService<IConfiguration>();
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.Services.AddSingleton(loggerFactory);
    // Add Azure OpenAI
    if (config["Azure:OpenAI:Endpoint"] != null && config["Azure:OpenAI:ApiKey"] != null && config["Azure:OpenAI:DeploymentName"] != null)
    {
        kernelBuilder.AddAzureOpenAIChatCompletion(
            endpoint: config["Azure:OpenAI:Endpoint"]!,
            apiKey: config["Azure:OpenAI:ApiKey"]!,
            deploymentName: config["Azure:OpenAI:DeploymentName"]!,
            serviceId: "aoai");
    }

    // Add GitHub Models
    if (config["GitHub:Models:GitHubToken"] != null && config["GitHub:Models:Endpoint"] != null && config["GitHub:Models:ModelId"] != null)
    {
        var credentials = new ApiKeyCredential(config["GitHub:Models:GitHubToken"]!);
        var options = new OpenAIClientOptions { Endpoint = new Uri(config["GitHub:Models:Endpoint"]!) };
        var client = new OpenAIClient(credentials, options);
        kernelBuilder.AddOpenAIChatCompletion(
            openAIClient: client,
            modelId: config["GitHub:Models:ModelId"]!,
            serviceId: "github");
    }

    var kernel = kernelBuilder.Build();

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    kernel.Plugins.AddFromFunctions("ntgmcpserver", tools.Select(x => x.AsKernelFunction()));
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    kernel.Plugins.Add(KernelPluginFactory.CreateFromType<DateTimePlugin>());

    return kernel;
});

builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<IKnowledgeService, KernelMemoryKnowledge>();
builder.Services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();


builder.Services.AddScoped<IKernelMemory>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var endpoint = Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__https__0") 
                   ?? Environment.GetEnvironmentVariable($"services__ntg-agent-knowledge__http__0") 
                   ?? throw new InvalidOperationException("KernelMemory Endpoint configuration is required");
    var apiKey = configuration["KernelMemory:ApiKey"] 
                ?? throw new InvalidOperationException("KernelMemory:ApiKey configuration is required");

    return new MemoryWebClient(endpoint, apiKey);
});

builder.Services.AddAuthentication("Identity.Application")
    .AddCookie("Identity.Application", option => option.Cookie.Name = ".AspNetCore.Identity.Application");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error-development");
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/error");
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();
