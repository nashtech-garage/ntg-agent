using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NTG.Agent.Shared.Logging.Metrics;
using Serilog;
using Serilog.Events;

namespace NTG.Agent.Shared.Logging.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationLogging(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Configure Serilog with enhanced features
        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithCorrelationId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", environment.ApplicationName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName)
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{CorrelationId}] [{Application}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: $"logs/{environment.ApplicationName}-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{CorrelationId}] [{Application}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 30);

        // Set minimum log level based on environment
        loggerConfig = environment.IsProduction()
            ? loggerConfig.MinimumLevel.Information()
            : loggerConfig.MinimumLevel.Debug();

        Log.Logger = loggerConfig.CreateLogger();

        services.AddSerilog();

        // Register our custom services
        services.AddScoped(typeof(IApplicationLogger<>), typeof(ApplicationLogger<>));
        services.AddScoped<IMetricsCollector, MetricsCollector>();

        return services;
    }

    public static IApplicationBuilder UseApplicationLogging(this IApplicationBuilder app)
    {
        app.UseMiddleware<ErrorTrackingMiddleware>();
        app.UseMiddleware<LoggingMiddleware>();
        return app;
    }


}
