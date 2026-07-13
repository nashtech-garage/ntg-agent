using Microsoft.EntityFrameworkCore;
using NTG.Agent.AITools.SearchOnlineTool.Extensions;
using NTG.Agent.MCP.Server.Data;
using NTG.Agent.MCP.Server.Services;
using NTG.Agent.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<MonkeyService>();
builder.Services.AddSingleton<WeatherService>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var apiKey = sp.GetRequiredService<IConfiguration>()["WeatherApi:ApiKey"];
    return new WeatherService(httpClient, apiKey);
});

// Factory registration so MCP resource handlers (resolved outside a request scope)
// can create contexts; it also registers a scoped SkillDbContext for controllers.
builder.Services.AddDbContextFactory<SkillDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .AddAiTool();

var app = builder.Build();

app.MapMcp();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();
