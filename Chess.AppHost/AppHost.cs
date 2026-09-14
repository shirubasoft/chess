var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.AddParameter("backend");
var orleansClusterId = builder.AddParameter("orleans-cluster-id");
var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("chess");

builder.AddProject<Projects.Chess_Server>("server")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Chess__Backend", backend)
    .WithEnvironment("Chess__Orleans__ClusterId", orleansClusterId)
    .WithEndpoint(name: "orleans-silo", scheme: "tcp", env: "Chess__Orleans__SiloPort", isProxied: false)
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
