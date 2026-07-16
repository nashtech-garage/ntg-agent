using NTG.Agent.AITools.SearchOnlineTool.Extensions;
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

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly()
    .AddAiTool();

var app = builder.Build();

app.MapMcp();

app.MapDefaultEndpoints();

app.Run();
