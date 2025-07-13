using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NTG.Agent.WebClient.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddBootstrapBlazor();

Uri baseUri = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddHttpClient<TestClient>(client =>
{
    client.BaseAddress = baseUri;
});

builder.Services.AddHttpClient<ChatClient>(client =>
{
    client.BaseAddress = baseUri;
});

builder.Services.AddHttpClient<ConversationClient>(client =>
{
    client.BaseAddress = baseUri;
});

await builder.Build().RunAsync();
