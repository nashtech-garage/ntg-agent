using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator;

public class DesignTimeAgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    private readonly string? _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    public AgentDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{_environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var builder = new DbContextOptionsBuilder<AgentDbContext>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        builder.UseSqlServer(connectionString, x => x.MigrationsAssembly(typeof(AgentDbContext).Assembly.FullName)
            .EnableRetryOnFailure());

        return new AgentDbContext(builder.Options);
    }
}

