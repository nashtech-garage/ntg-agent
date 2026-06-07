using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server");
var knowledge = builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge");

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithExternalHttpEndpoints()
    .WithReference(mcpServer)
    .WithReference(knowledge);

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

StartMyCopilotAppDevServer();

builder.Build().Run();

static void StartMyCopilotAppDevServer()
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Development";

    if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var repoRoot = FindRepositoryRoot();
    if (repoRoot is null)
    {
        Console.WriteLine("Could not locate repository root to start my-copilot-app.");
        return;
    }

    var appPath = Path.Combine(repoRoot.FullName, "my-copilot-app");
    var packageJson = Path.Combine(appPath, "package.json");
    if (!File.Exists(packageJson))
    {
        Console.WriteLine($"my-copilot-app package.json not found at {appPath}. Skipping frontend startup.");
        return;
    }

    try
    {
        Console.WriteLine($"Starting my-copilot-app dev server in {appPath}...");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "npm",
            Arguments = "run dev",
            WorkingDirectory = appPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = Process.Start(processStartInfo);
        if (process is null)
        {
            Console.WriteLine("Failed to start my-copilot-app dev server. Process returned null.");
            return;
        }

        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine($"[my-copilot-app] {args.Data}");
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.Error.WriteLine($"[my-copilot-app] {args.Data}");
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to start my-copilot-app dev server: {ex.Message}");
    }
}

static DirectoryInfo? FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "my-copilot-app")))
        {
            return directory;
        }

        directory = directory.Parent;
    }

    return null;
}
