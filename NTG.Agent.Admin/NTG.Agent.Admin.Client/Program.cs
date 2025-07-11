using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NTG.Agent.Admin.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddHttpClient<TestClient>(client =>
{
    client.BaseAddress = new("https://localhost:7097");
});

await builder.Build().RunAsync();
