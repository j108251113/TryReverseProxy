var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.TryReverseProxy>("tryreverseproxy");

builder.Build().Run();
