using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

var saPassword = builder.AddParameter("sql-sa-password", secret: true);
var googleApiKey = builder.AddParameter("google-api-key", secret: true);
var googleSearchId = builder.AddParameter("google-search-engine-id", secret: true);
var pgPassword = builder.AddParameter("lightrag-pg-password", secret: true);
var lightragApiKey = builder.AddParameter("lightrag-api-key", secret: true);
// Dedicated Azure key for LightRAG — used for BOTH its embedding and LLM bindings (the
// hcm resource exposes one key for chat + embeddings).
var lightragEmbeddingApiKey = builder.AddParameter("lightrag-embedding-api-key", secret: true);

// LightRAG + its Postgres live on a dedicated Ubuntu server reached directly over TLS.
// All default to empty = plain local run (local Docker socket, localhost:5432); set them
// in user-secrets to target the remote host instead.
var lightragDockerHost = builder.AddParameter("lightrag-docker-host", secret: true);          // e.g. https://4.193.109.6:2376
var lightragCertPath = builder.AddParameter("lightrag-docker-cert-path", secret: true);       // path to client.pfx
var lightragCertPassword = builder.AddParameter("lightrag-docker-cert-password", secret: true);
var lightragServerHost = builder.AddParameter("lightrag-server-host", secret: true);          // e.g. 4.193.109.6
var lightragGatewayUrl = builder.AddParameter("lightrag-gateway-url", secret: true);          // e.g. https://4.193.109.6; empty => http://localhost:8080
var lightragPostgresPort = builder.AddParameter("lightrag-postgres-port", secret: true);      // 5432 direct

var sql = builder.AddSqlServer("sqlserver", password: saPassword)
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

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server")
	.WithEnvironment("Google__ApiKey", googleApiKey)
	.WithEnvironment("Google__SearchEngineId", googleSearchId);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
	.WithExternalHttpEndpoints()
	.WithReference(mcpServer)
	.WaitForCompletion(migrateOrchestrator)
	// The Orchestrator spawns per-agent LightRAG containers on the remote Ubuntu
	// Docker daemon (over the SSH tunnel) against the standalone Postgres there. That
	// server is provisioned independently, so there is no local resource to wait on.
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	// LightRAG per-agent container provisioning config (see LightRagSettings /
	// LightRagContainerManager). These replace the old singleton "lightrag" container
	// env — the Orchestrator now applies them to each spawned lightrag-agent-{id}.
	.WithEnvironment("LightRag__ApiKey", lightragApiKey)
	.WithEnvironment("LightRag__ImageRef", "ghcr.io/hkuds/lightrag")
	.WithEnvironment("LightRag__ImageTag", "v1.4.16")
	.WithEnvironment("LightRag__PostgresHostAlias", "lightrag-postgres")
	.WithEnvironment("LightRag__PostgresPassword", pgPassword)
	.WithEnvironment("LightRag__PostgresDatabase", "uploaded-documents")
	// Remote Ubuntu server, reached directly over TLS: the Docker daemon on :2376 with a
	// client certificate, and the nginx gateway on :443 which routes /agents/{agentId}/* to
	// that agent's container by name over the Docker network (containers publish no host
	// ports). Inbound access is gated by the Azure NSG rules. All default to empty for a
	// plain local run (local Docker socket + the deploy/lightrag-local gateway).
	.WithEnvironment("LightRag__DockerHost", lightragDockerHost)
	.WithEnvironment("LightRag__DockerCertPath", lightragCertPath)
	.WithEnvironment("LightRag__DockerCertPassword", lightragCertPassword)
	.WithEnvironment("LightRag__ServerHost", lightragServerHost)
	.WithEnvironment("LightRag__GatewayUrl", lightragGatewayUrl)
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
