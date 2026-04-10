using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var garage = builder.AddGarage("garage")
    .WithDataVolume();

var apiService = builder.AddProject<CommunityToolkit_Aspire_Hosting_Garage_ApiService>("apiservice")
    .WithReference(garage)
    .WaitFor(garage)
    .WithHttpHealthCheck("/health");

builder.AddProject<CommunityToolkit_Aspire_Hosting_Garage_Web>("web")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
