using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NTG.Agent.MCP.Server.Data;

namespace NTG.Agent.MCP.Server;

public class DesignTimeSkillDbContextFactory : IDesignTimeDbContextFactory<SkillDbContext>
{
    private readonly string? _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    public SkillDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{_environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var builder = new DbContextOptionsBuilder<SkillDbContext>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        builder.UseSqlServer(connectionString, x => x.MigrationsAssembly(typeof(SkillDbContext).Assembly.FullName)
            .EnableRetryOnFailure());

        return new SkillDbContext(builder.Options);
    }
}
