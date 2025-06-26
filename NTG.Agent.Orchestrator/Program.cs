using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Agents;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<Kernel>(serviceBuilder => { 
    var config = serviceBuilder.GetRequiredService<IConfiguration>();
    var kernelBuilder = Kernel.CreateBuilder();

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
    return kernel;
});

builder.Services.AddScoped<IAgentService, AgentService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();
