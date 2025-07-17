using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Plugins;
using OpenAI;
using System.ClientModel;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlite("Data Source=test.db"));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("../key/"))
    .SetApplicationName("NTGAgent");

// Simple kernel without external dependencies for testing
builder.Services.AddSingleton<Kernel>(serviceBuilder => { 
    var kernelBuilder = Kernel.CreateBuilder();
    var kernel = kernelBuilder.Build();
    return kernel;
});

builder.Services.AddScoped<IAgentService, AgentService>();

// Allow anonymous access for testing
builder.Services.AddAuthentication()
    .AddCookie("Test", options => {});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAssertion(_ => true) // Allow all for testing
        .Build();
});

var app = builder.Build();

// Create database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    context.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
