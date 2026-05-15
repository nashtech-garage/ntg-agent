using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.AnonymousSessions;
using NTG.Agent.Orchestrator.Models.Configuration;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
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

builder.Services.Configure<LongTermMemorySettings>(builder.Configuration.GetSection("LongTermMemory"));
builder.Services.Configure<DocumentIntelligenceSettings>(builder.Configuration.GetSection("Azure:DocumentIntelligence"));
builder.Services.Configure<LightRagSettings>(builder.Configuration.GetSection("LightRag"));

builder.Services.AddControllers();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("../key/"))
    .SetApplicationName("NTGAgent");

builder.Services.Configure<AnonymousUserSettings>(
    builder.Configuration.GetSection("AnonymousUserSettings"));

builder.Services.AddScoped<IAgentFactory,AgentFactory>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<IKnowledgeService, LightRagKnowledge>();
builder.Services.AddScoped<IUserMemoryService, UserMemoryService>();
builder.Services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();
builder.Services.AddScoped<ITokenTrackingService, TokenTrackingService>();
builder.Services.AddScoped<IAnonymousSessionService, AnonymousSessionService>();
builder.Services.AddScoped<IIpAddressService, IpAddressService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<LightRagFileStore>(sp =>
{
    var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
    var log = sp.GetRequiredService<ILogger<LightRagFileStore>>();
    return new LightRagFileStore(cfg.FileStorePath, log);
});

// LightRAG /query invokes an LLM and routinely takes >10s, exceeding the
// standard resilience handler's 10s per-attempt timeout. Override the named
// options for this client so the global ServiceDefaults pipeline is reused
// with longer timeouts and no retries (retrying slow LLM calls is wasteful).
builder.Services.Configure<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(
    nameof(LightRagClient),
    o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        o.Retry.MaxRetryAttempts = 0;
    });

builder.Services.AddHttpClient<LightRagClient>((sp, c) =>
{
    var cfg = sp.GetRequiredService<IOptions<LightRagSettings>>().Value;
    c.BaseAddress = new Uri(cfg.Endpoint);
    c.Timeout = TimeSpan.FromMinutes(5);
    if (!string.IsNullOrEmpty(cfg.ApiKey))
        c.DefaultRequestHeaders.Add("X-API-Key", cfg.ApiKey);
});

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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Process X-Forwarded-For and X-Forwarded-Proto headers
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Clear default networks and proxies - be explicit about what we trust
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    // TODO: In production, configure specific known proxies/load balancers:
    // options.KnownProxies.Add(IPAddress.Parse("10.0.0.100"));
    // options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    
    // For Azure App Service, Azure Front Door, or other cloud services:
    // The proxy IP addresses may be dynamic, so you may need to trust all proxies
    // ONLY do this if your application is not directly accessible from the internet
    // and all traffic goes through your trusted infrastructure
    // Uncomment the following line only if needed:
    // options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("0.0.0.0"), 0));
    // options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("::"), 0));
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

app.UseForwardedHeaders();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();
