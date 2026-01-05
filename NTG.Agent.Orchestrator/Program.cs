using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.KernelMemory;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Knowledge;
using NTG.Agent.Orchestrator.Services.Memory;
using NTG.Agent.Orchestrator.Services.TokenTracking;
using NTG.Agent.ServiceDefaults;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

const string SourceName = "NTG.Agent.Orchestrator";
const string ServiceName = "Orchestrator";

var builder = WebApplication.CreateBuilder(args);

// Endpoint to the Aspire Dashboard
var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? throw new ConfigurationException("OTEL_EXPORTER_OTLP_ENDPOINT configuration key is required but not found");

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(ServiceName);

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(SourceName)
    .AddSource("*Microsoft.Extensions.AI") // Listen to the Experimental.Microsoft.Extensions.AI source for chat client telemetry
    .AddSource("*Microsoft.Extensions.Agents*") // Listen to the Experimental.Microsoft.Extensions.Agents source for agent telemetry
    .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint))
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(SourceName)
    .AddMeter("*Microsoft.Agents.AI") // Agent Framework metrics
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

using var activitySource = new ActivitySource(SourceName);
using var meter = new Meter(SourceName);

// Create custom metrics
var interactionCounter = meter.CreateCounter<int>("agent_interactions_total", description: "Total number of agent interactions");
var responseTimeHistogram = meter.CreateHistogram<double>("agent_response_time_seconds", description: "Agent response time in seconds");

builder.AddServiceDefaults();

builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("../key/"))
    .SetApplicationName("NTGAgent");

builder.Services.AddScoped<IAgentFactory, AgentFactory>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<IKnowledgeService, KernelMemoryKnowledge>();
builder.Services.AddScoped<IUserMemoryService, UserMemoryService>();
builder.Services.AddScoped<ITokenTrackingService, TokenTrackingService>();

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
