var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Aiakos_Orchestrator>("orchestrator");
builder.Build().Run();
