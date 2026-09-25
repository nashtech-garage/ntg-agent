using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using NTG.Agent.AITools.SearchOnlineTool.Services;

namespace NTG.Agent.AITools.SearchOnlineTool.Extensions;
public static class AiToolRegistrationExtensions
{
    public static IMcpServerBuilder AddAiTool(this IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithToolsFromAssembly(typeof(AiToolRegistrationExtensions).Assembly);

        var services = mcpServerBuilder.Services;
        services.AddSingleton<ITextSearchService, GoogleTextSearchService>();
        services.AddSingleton<IWebScraper, WebScraper>();

        // register GoogleTextSearch as ITextSearch
        services.AddSingleton<ITextSearch>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            // read settings
            var apiKey = configuration["Google:ApiKey"]
                ?? throw new InvalidOperationException("Google:ApiKey is missing.");
            var cseId = configuration["Google:SearchEngineId"]
                ?? throw new InvalidOperationException("Google:SearchEngineId is missing.");
#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            return new GoogleTextSearch(
                initializer: new() { ApiKey = apiKey },
                searchEngineId: cseId);
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        });

        return mcpServerBuilder;
    }
}