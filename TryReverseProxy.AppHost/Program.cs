var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.TryReverseProxy>("tryreverseproxy");

builder.AddProject<Projects.ReverseProxy>("reverseproxy");

builder.Build().Run();
