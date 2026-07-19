using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

var saPassword = builder.AddParameter("sql-sa-password", secret: true);
var githubToken = builder.AddParameter("github-token", secret: true);
var googleApiKey = builder.AddParameter("google-api-key", secret: true);
var googleSearchId = builder.AddParameter("google-search-engine-id", secret: true);
var pgPassword = builder.AddParameter("lightrag-pg-password", secret: true);
var lightragApiKey = builder.AddParameter("lightrag-api-key", secret: true);
// Dedicated Azure key for LightRAG — used for BOTH its embedding and LLM bindings (the
// hcm resource exposes one key for chat + embeddings).
var lightragEmbeddingApiKey = builder.AddParameter("lightrag-embedding-api-key", secret: true);

// LightRAG + its Postgres can live on a dedicated Ubuntu server reached over an SSH tunnel.
// All three default to empty = plain local run (local Docker socket, localhost:5432);
// set them in user-secrets to target the remote host instead.
var lightragDockerHost = builder.AddParameter("lightrag-docker-host", secret: true);      // e.g. tcp://localhost:2375 (ssh -L)
var lightragSocksProxy = builder.AddParameter("lightrag-socks-proxy", secret: true);      // e.g. socks5://localhost:1080 (ssh -D)
var lightragPostgresPort = builder.AddParameter("lightrag-postgres-port", secret: true); // 55432 over the tunnel

var sql = builder.AddSqlServer("sqlserver", password: saPassword)
				 // 2022-latest crash-loops on WSL2 kernel 6.6.x (SQLPAL fatal error in lsass at startup);
				 // 2025-latest is required on this environment.
				 .WithImageTag("2022-latest")
				 .WithEndpoint("tcp", endpoint =>
				 {
					 endpoint.Port = 1433;
					 endpoint.TargetPort = 1433;
				 })
				 .WithDataVolume("ntg-agent-local-dev-sqlserver-data");

if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
	sql.WithContainerRuntimeArgs("--platform", "linux/amd64");

var db = sql.AddDatabase("NTGAgent");

// Separate database owned by the MCP server: the admin-uploaded Agent Skills catalog.
var skillsDb = sql.AddDatabase("NTGAgentSkills");

var migrateAdmin = builder.AddExecutable(
		"db-migrate-admin",
		"dotnet",
		workingDirectory: "..",
		"ef", "database", "update",
		"--project", "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj",
		"--startup-project", "NTG.Agent.Admin/NTG.Agent.Admin/NTG.Agent.Admin.csproj")
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WaitFor(db);

var migrateOrchestrator = builder.AddExecutable(
		"db-migrate-orchestrator",
		"dotnet",
		workingDirectory: "..",
		"ef", "database", "update",
		"--project", "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj",
		"--startup-project", "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj",
		// The Orchestrator has two DbContexts since the AG-UI merge; AppIdentityDbContext
		// owns no migrations (Identity schema belongs to the WebClient), so migrate AgentDbContext.
		"--context", "AgentDbContext")
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WaitForCompletion(migrateAdmin);

// Chained after the other migrations so only one `dotnet ef` runs at a time.
var migrateMcpServer = builder.AddExecutable(
		"db-migrate-mcp-server",
		"dotnet",
		workingDirectory: "..",
		"ef", "database", "update",
		"--project", "NTG.Agent.MCP.Server/NTG.Agent.MCP.Server.csproj",
		"--startup-project", "NTG.Agent.MCP.Server/NTG.Agent.MCP.Server.csproj",
		"--context", "SkillDbContext")
	.WithEnvironment("ConnectionStrings__DefaultConnection", skillsDb)
	.WaitForCompletion(migrateOrchestrator);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server")
	.WithEnvironment("Google__ApiKey", googleApiKey)
	.WithEnvironment("Google__SearchEngineId", googleSearchId)
	.WithEnvironment("ConnectionStrings__DefaultConnection", skillsDb)
	.WaitForCompletion(migrateMcpServer);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
	.WithExternalHttpEndpoints()
	.WithReference(mcpServer)
	.WaitForCompletion(migrateOrchestrator)
	// The Orchestrator spawns per-agent LightRAG containers on the remote Ubuntu
	// Docker daemon (over the SSH tunnel) against the standalone Postgres there. That
	// server is provisioned independently, so there is no local resource to wait on.
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WithEnvironment("GitHub__Models__GitHubToken", githubToken)
	// LightRAG per-agent container provisioning config (see LightRagSettings /
	// LightRagContainerManager). These replace the old singleton "lightrag" container
	// env — the Orchestrator now applies them to each spawned lightrag-agent-{id}.
	.WithEnvironment("LightRag__ApiKey", lightragApiKey)
	.WithEnvironment("LightRag__ImageRef", "ghcr.io/hkuds/lightrag")
	.WithEnvironment("LightRag__ImageTag", "v1.4.16")
	.WithEnvironment("LightRag__PostgresHostAlias", "lightrag-postgres")
	.WithEnvironment("LightRag__PostgresPassword", pgPassword)
	.WithEnvironment("LightRag__PostgresDatabase", "uploaded-documents")
	// Remote Ubuntu server (over the SSH tunnel): drive its Docker daemon via the
	// forwarded socket and reach the per-agent container ports through the SOCKS proxy.
	// Both default to empty for a plain local run.
	.WithEnvironment("LightRag__DockerHost", lightragDockerHost)
	.WithEnvironment("LightRag__SocksProxy", lightragSocksProxy)
	.WithEnvironment("LightRag__PostgresPort", lightragPostgresPort)
	.WithEnvironment("LightRag__LlmModel", "gpt-5.1")
	.WithEnvironment("LightRag__LlmEndpoint", "https://rmit-capstone-2026-hcm--resource.openai.azure.com/")
	.WithEnvironment("LightRag__LlmApiKey", lightragEmbeddingApiKey)
	.WithEnvironment("LightRag__EmbeddingModel", "text-embedding-3-large")
	.WithEnvironment("LightRag__EmbeddingEndpoint", "https://rmit-capstone-2026-hcm--resource.openai.azure.com/")
	.WithEnvironment("LightRag__EmbeddingApiKey", lightragEmbeddingApiKey)
	.WithEnvironment("LightRag__AzureApiVersion", "2024-08-01-preview")
	.WithEnvironment("LightRag__EmbeddingDim", "1536")
	.WithEnvironment("LightRag__ChunkSize", "1500")
	.WithEnvironment("LightRag__ChunkOverlap", "100")
	.WithEnvironment("LightRag__MaxAsync", "8")
	.WithEnvironment("LightRag__MaxParallelInsert", "2")
	.WithEnvironment("LightRag__EmbeddingFuncMaxAsync", "8");

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

// CopilotKit chat frontend (AG-UI). Aspire installs packages and runs the "dev" script in run
// mode, and produces a standalone-output container in publish mode.
builder.AddNextJsApp("ntg-agent-ag-ui-webclient", "../my-copilot-app")
    .WithReference(orchestrator)
    .WaitFor(orchestrator)
    // route.ts resolves the backend via ORCHESTRATOR_URL (service-discovery env vars contain
    // dashes from the resource name, which the Next.js code does not read).
    .WithEnvironment("ORCHESTRATOR_URL", orchestrator.GetEndpoint("https"))
    .WithExternalHttpEndpoints();

builder.Build().Run();
