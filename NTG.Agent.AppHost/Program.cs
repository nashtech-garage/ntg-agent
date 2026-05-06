using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

var saPassword         = builder.AddParameter("sql-sa-password", "Admin123_Strong!", secret: true);
var githubToken        = builder.AddParameter("github-token",            secret: true);
var kernelMemoryApiKey = builder.AddParameter("kernel-memory-api-key",   secret: true);
var googleApiKey       = builder.AddParameter("google-api-key",          secret: true);
var googleSearchId     = builder.AddParameter("google-search-engine-id", secret: true);

var sql = builder.AddSqlServer("sqlserver", password: saPassword)
                 .WithImageTag("2025-latest")
                 .WithEndpoint("tcp", endpoint =>
                 {
                     endpoint.Port = 1433;
                     endpoint.TargetPort = 1433;
                 })
                 .WithDataVolume("ntg-agent-local-dev-sqlserver-data");

if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
    sql.WithContainerRuntimeArgs("--platform", "linux/amd64");


var db  = sql.AddDatabase("NTGAgent");

var elasticsearch = builder.AddElasticsearch("elasticsearch")
                           .WithImageTag("8.15.0")
                           .WithEndpoint("http", endpoint =>
                           {
                               endpoint.Port = 9200;
                               endpoint.TargetPort = 9200;
                           })
                           .WithDataVolume("ntg-agent-local-dev-elasticsearch-data");

var migrateAdmin = builder.AddExecutable(
        "db-migrate-admin",
        "dotnet",
        workingDirectory: "..",
        "ef", "database", "update",
        "--project",         "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj",
        "--startup-project", "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj")
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WaitFor(db);

var migrateOrchestrator = builder.AddExecutable(
        "db-migrate-orchestrator",
        "dotnet",
        workingDirectory: "..",
        "ef", "database", "update",
        "--project",         "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj",
        "--startup-project", "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj")
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WaitForCompletion(migrateAdmin);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server")
    .WithEnvironment("Google__ApiKey",         googleApiKey)
    .WithEnvironment("Google__SearchEngineId", googleSearchId);

var knowledge = builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge")
    .WaitFor(db)
    .WaitFor(elasticsearch)
    .WithEnvironment("KernelMemory__Services__SqlServer__ConnectionString",  db)
    .WithEnvironment("KernelMemory__Services__OpenAI__APIKey",               githubToken)
    .WithEnvironment("KernelMemory__ServiceAuthorization__AccessKey1",       kernelMemoryApiKey)
    .WithEnvironment("KernelMemory__ServiceAuthorization__AccessKey2",       kernelMemoryApiKey)
    .WithEnvironment("KernelMemory__Services__Elasticsearch__Endpoint",      elasticsearch.GetEndpoint("http"))
    .WithEnvironment("KernelMemory__Services__Elasticsearch__UserName",      "elastic")
    .WithEnvironment("KernelMemory__Services__Elasticsearch__Password",      elasticsearch.Resource.PasswordParameter);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithExternalHttpEndpoints()
    .WithReference(mcpServer)
    .WithReference(knowledge)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db)
    .WithEnvironment("KernelMemory__ApiKey",                 kernelMemoryApiKey)
    .WithEnvironment("GitHub__Models__GitHubToken",          githubToken);

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db);

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator)
    .WaitForCompletion(migrateOrchestrator)
    .WithEnvironment("ConnectionStrings__DefaultConnection", db);

builder.Build().Run();
