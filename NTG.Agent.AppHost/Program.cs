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

var lightrag = builder.AddContainer("lightrag", "ghcr.io/hkuds/lightrag", "v1.4.16")
	.WithHttpEndpoint(port: 9621, targetPort: 9621, name: "http")
	.WithEnvironment("LIGHTRAG_KV_STORAGE", "PGKVStorage")
	.WithEnvironment("LIGHTRAG_VECTOR_STORAGE", "PGVectorStorage")
	.WithEnvironment("LIGHTRAG_GRAPH_STORAGE", "PGGraphStorage")
	.WithEnvironment("LIGHTRAG_DOC_STATUS_STORAGE", "PGDocStatusStorage")
	.WithEnvironment("POSTGRES_HOST", "lightrag-postgres")
	.WithEnvironment("POSTGRES_PORT", "5432")
	.WithEnvironment("POSTGRES_USER", "postgres")
	.WithEnvironment("POSTGRES_PASSWORD", pgPassword)
	.WithEnvironment("POSTGRES_DATABASE", "uploaded-documents")
	// Azure OpenAI: no 8000-token request cap and ~1M TPM / 10K RPM on standard tier.
	// gpt-5.4-mini is the deployment used for entity extraction and merge-summary
	// (high volume, low reasoning cost); save full gpt-5.4 for chat agents.
	.WithEnvironment("LLM_BINDING", "azure_openai")
	.WithEnvironment("LLM_MODEL", "gpt-5.4-mini")
	.WithEnvironment("LLM_BINDING_HOST", "https://rmit-capstone-2026-resource.cognitiveservices.azure.com/")
	.WithEnvironment("LLM_BINDING_API_KEY", azureOpenAiApiKey)
	.WithEnvironment("AZURE_OPENAI_API_VERSION", "2024-08-01-preview")
	// text-embedding-3-large is 3072-dim (vs text-embedding-3-small's 1536); the
	// pgvector schema is created on first ingestion against EMBEDDING_DIM, so any
	// future dim change requires wiping ntg-agent-local-dev-lightrag-postgres-data.
	.WithEnvironment("EMBEDDING_BINDING", "azure_openai")
	.WithEnvironment("EMBEDDING_MODEL", "text-embedding-3-large")
	.WithEnvironment("EMBEDDING_BINDING_HOST", "https://rmit-capstone-2026-ext-resource.cognitiveservices.azure.com/")
	.WithEnvironment("EMBEDDING_BINDING_API_KEY", azureEmbeddingApiKey)
	.WithEnvironment("EMBEDDING_DIM", "3072")
	.WithEnvironment("AZURE_EMBEDDING_API_VERSION", "2024-08-01-preview")
	// Larger chunks (recommended top-of-range is 1500) and full default extraction
	// budget — Azure OpenAI has no per-request token cap to worry about here.
	.WithEnvironment("CHUNK_SIZE", "1500")
	.WithEnvironment("CHUNK_OVERLAP_SIZE", "100")
	.WithEnvironment("MAX_ASYNC", "8")
	.WithEnvironment("MAX_PARALLEL_INSERT", "2")
	.WithEnvironment("EMBEDDING_FUNC_MAX_ASYNC", "8")
	.WithEnvironment("LIGHTRAG_API_KEY", lightragApiKey)
	.WaitFor(lightragPostgres);

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
	.WithEnvironment("ConnectionStrings__DefaultConnection", db)
	.WithEnvironment("KernelMemory__ApiKey", kernelMemoryApiKey)
	.WithEnvironment("GitHub__Models__GitHubToken", githubToken)
	.WithEnvironment("LightRag__Endpoint", lightrag.GetEndpoint("http"))
	.WithEnvironment("LightRag__ApiKey", lightragApiKey);

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
