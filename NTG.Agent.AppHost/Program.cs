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

var lightragPostgres = builder.AddPostgres("lightrag-postgres", password: pgPassword)
								.WithDockerfile("../scripts/lightrag-postgres")
								.WithDataVolume("ntg-agent-local-dev-lightrag-postgres-data")
								.WithBindMount("../scripts/lightrag-pg-init", "/docker-entrypoint-initdb.d");

// Custom image at scripts/lightrag-postgres layers Apache AGE on top of
// pgvector/pgvector:0.8.2-pg17-trixie so the same instance can serve vector
// and graph RAG.
// Data volume: "ntg-agent-local-dev-lightrag-postgres-data"

if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
	lightragPostgres.WithContainerRuntimeArgs("--platform", "linux/amd64");

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
	// text-embedding-3-large (3072-dim) on the dedicated embedding endpoint; text gen
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
	// The Orchestrator spawns per-agent LightRAG containers against the shared
	// Postgres, so it must wait for Postgres to be ready before its reconciler runs.
	.WaitFor(lightragPostgres)
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
	.WithEnvironment("LightRag__LlmModel", "gpt-5.4")
	.WithEnvironment("LightRag__LlmEndpoint", "https://rmit-capstone-2026-resource.cognitiveservices.azure.com/")
	.WithEnvironment("LightRag__LlmApiKey", azureOpenAiApiKey)
	.WithEnvironment("LightRag__EmbeddingModel", "text-embedding-3-large")
	.WithEnvironment("LightRag__EmbeddingEndpoint", "https://rmit-capstone-2026-ext-resource.cognitiveservices.azure.com/")
	.WithEnvironment("LightRag__EmbeddingApiKey", azureEmbeddingApiKey)
	.WithEnvironment("LightRag__AzureApiVersion", "2024-08-01-preview")
	.WithEnvironment("LightRag__EmbeddingDim", "3072")
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
