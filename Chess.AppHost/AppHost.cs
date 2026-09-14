var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.AddParameter("backend", "Akka");
var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("chess");

builder.AddProject<Projects.Chess_Server>("server")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Chess__Backend", backend)
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
