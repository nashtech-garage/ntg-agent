var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server");

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithReference(mcpServer)
    .WaitFor(mcpServer);

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge");

builder.Build().Run();
