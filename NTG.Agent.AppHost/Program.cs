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
