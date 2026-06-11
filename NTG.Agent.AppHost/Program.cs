using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var saPassword = builder.AddParameter("sql-sa-password", "Admin123_Strong!", secret: true);
var githubToken = builder.AddParameter("github-token", secret: true);
var kernelMemoryApiKey = builder.AddParameter("kernel-memory-api-key", secret: true);
var googleApiKey = builder.AddParameter("google-api-key", secret: true);
var googleSearchId = builder.AddParameter("google-search-engine-id", secret: true);
var pgPassword = builder.AddParameter("lightrag-pg-password", secret: true);
var lightragApiKey = builder.AddParameter("lightrag-api-key", secret: true);
var azureOpenAiApiKey = builder.AddParameter("azure-openai-api-key", secret: true);
var azureEmbeddingApiKey = builder.AddParameter("azure-embedding-api-key", secret: true);

// LightRAG + its Postgres now live on a dedicated Ubuntu server reached over an SSH
// tunnel. The dev sets these for the remote setup; both default to empty so a plain
// local run uses the local Docker socket and a direct connection. The other LightRag
// host settings (ServerHost=localhost, PortBindHostIp=127.0.0.1, PostgresHost->ServerHost,
// PostgresPort=5432) already default correctly for the SSH tunnel, so they are not set here.
var lightragDockerHost = builder.AddParameter("lightrag-docker-host", "");      // e.g. tcp://localhost:2375 (ssh -L)
var lightragSocksProxy = builder.AddParameter("lightrag-socks-proxy", "");      // e.g. socks5://localhost:1080 (ssh -D)
// Local port the Orchestrator dials for Postgres (the reset path) — matches the
// `ssh -L <thisPort>:127.0.0.1:5432` forward. Default 5432; raise it (e.g. 55432)
// if the Mac already runs a local Postgres on 5432.
var lightragPostgresPort = builder.AddParameter("lightrag-postgres-port", "5432");

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

// NOTE: The pgvector + Apache AGE Postgres no longer runs here. It now runs
// standalone on the Ubuntu server via deploy/lightrag-postgres/docker-compose.yml
// (same scripts/lightrag-postgres image + scripts/lightrag-pg-init SQL). The
// Orchestrator reaches it over the SSH tunnel; pgPassword below is still passed to
// it as LightRag__PostgresPassword.

// NOTE: There is no longer a singleton "lightrag" container here. Each agent now
// gets its own dedicated lightrag-agent-{agentId} app container, spun up on demand
// by the Orchestrator (LightRagContainerManager / LightRagReconcilerHostedService)
// via the host Docker daemon. They all point at the single shared lightrag-postgres
// above and are isolated by LightRAG's WORKSPACE env var. The Azure OpenAI bindings,
// chunk knobs, Postgres password and API key that used to live here are passed to the
// Orchestrator below as LightRag__* env vars and re-applied per spawned container.

var elasticsearch = builder.AddElasticsearch("elasticsearch")
						   .WithImageTag("8.15.0")
						   .WithEndpoint("http", endpoint =>
						   {
							   endpoint.Port = 9200;
							   endpoint.TargetPort = 9200;
						   })
						   .WithDataVolume("ntg-agent-local-dev-elasticsearch-data");

builder.Eventing.Subscribe<ResourceReadyEvent>(elasticsearch.Resource, async (evt, ct) =>
{
	var password = await elasticsearch.Resource.PasswordParameter.GetValueAsync(ct);
	var endpoint = elasticsearch.GetEndpoint("http").Url;

	using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
	http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
		"Basic",
		Convert.ToBase64String(Encoding.UTF8.GetBytes($"elastic:{password}")));

	for (var attempt = 0; attempt < 90; attempt++)
	{
		try
		{
			var response = await http.PostAsJsonAsync(
				"/_security/user/kibana_system/_password",
				new { password },
				ct);
			if (response.IsSuccessStatusCode) return;
		}
		catch (HttpRequestException) { }
		await Task.Delay(TimeSpan.FromSeconds(2), ct);
	}

	throw new InvalidOperationException(
		"Failed to set the kibana_system password on Elasticsearch after 180s.");
});

var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "8.15.0")
	.WithHttpEndpoint(port: 5601, targetPort: 5601, name: "http")
	.WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
	.WithEnvironment("ELASTICSEARCH_USERNAME", "kibana_system")
	.WithEnvironment("ELASTICSEARCH_PASSWORD", elasticsearch.Resource.PasswordParameter)
	.WaitFor(elasticsearch);

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
		"--startup-project", "NTG.Agent.Orchestrator/NTG.Agent.Orchestrator.csproj")
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WaitForCompletion(migrateAdmin);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server")
	.WithEnvironment("Google__ApiKey", googleApiKey)
	.WithEnvironment("Google__SearchEngineId", googleSearchId);

var knowledge = builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge")
	.WaitFor(db)
	.WaitFor(elasticsearch)
	.WithEnvironment("KernelMemory__Services__SqlServer__ConnectionString", db)
	// KM long-term memory now runs on Azure OpenAI to avoid the GitHub Models 8k/RPM
	// caps that were causing /search to time out via MemoryWebClient. Embeddings use
	// text-embedding-3-large (1536-dim, truncated via MRL) on the dedicated embedding endpoint; text gen
	// uses gpt-5.4-mini on the shared chat endpoint. Switching embedding dim requires
	// wiping the Elasticsearch data volume so the index can be recreated. The OpenAI
	// section in appsettings.Development.json is left as a dormant fallback (no key).
	.WithEnvironment("KernelMemory__Services__AzureOpenAIEmbedding__Endpoint", "https://rmit-capstone-2026-ext-resource.cognitiveservices.azure.com/")
	.WithEnvironment("KernelMemory__Services__AzureOpenAIEmbedding__APIKey", azureEmbeddingApiKey)
	.WithEnvironment("KernelMemory__Services__AzureOpenAIText__Endpoint", "https://rmit-capstone-2026-resource.cognitiveservices.azure.com/")
	.WithEnvironment("KernelMemory__Services__AzureOpenAIText__APIKey", azureOpenAiApiKey)
	.WithEnvironment("KernelMemory__ServiceAuthorization__AccessKey1", kernelMemoryApiKey)
	.WithEnvironment("KernelMemory__ServiceAuthorization__AccessKey2", kernelMemoryApiKey)
	.WithEnvironment("KernelMemory__Services__Elasticsearch__Endpoint", elasticsearch.GetEndpoint("http"))
	.WithEnvironment("KernelMemory__Services__Elasticsearch__UserName", "elastic")
	.WithEnvironment("KernelMemory__Services__Elasticsearch__Password", elasticsearch.Resource.PasswordParameter);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
	.WithExternalHttpEndpoints()
	.WithReference(mcpServer)
	.WithReference(knowledge)
	.WaitForCompletion(migrateOrchestrator)
	// The Orchestrator spawns per-agent LightRAG containers on the remote Ubuntu
	// Docker daemon (over the SSH tunnel) against the standalone Postgres there. That
	// server is provisioned independently, so there is no local resource to wait on.
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WithEnvironment("KernelMemory__ApiKey", kernelMemoryApiKey)
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
	.WithEnvironment("LightRag__LlmModel", "gpt-5.4")
	.WithEnvironment("LightRag__LlmEndpoint", "https://rmit-capstone-2026-resource.cognitiveservices.azure.com/")
	.WithEnvironment("LightRag__LlmApiKey", azureOpenAiApiKey)
	.WithEnvironment("LightRag__EmbeddingModel", "text-embedding-3-large")
	.WithEnvironment("LightRag__EmbeddingEndpoint", "https://rmit-capstone-2026-ext-resource.cognitiveservices.azure.com/")
	.WithEnvironment("LightRag__EmbeddingApiKey", azureEmbeddingApiKey)
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

builder.Build().Run();
