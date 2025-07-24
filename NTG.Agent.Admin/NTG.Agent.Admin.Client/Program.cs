using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NTG.Agent.Admin.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

Uri baseUri = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddHttpClient<AgentClient>(client =>
{
    client.BaseAddress = baseUri;
});

builder.Services.AddHttpClient<DocumentClient>(client =>
{
    client.BaseAddress = baseUri;
});

await builder.Build().RunAsync();
