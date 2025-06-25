var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator");

builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient");

builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin");

builder.Build().Run();
