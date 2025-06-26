var builder = DistributedApplication.CreateBuilder(args);

var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator");

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin");

builder.Build().Run();
