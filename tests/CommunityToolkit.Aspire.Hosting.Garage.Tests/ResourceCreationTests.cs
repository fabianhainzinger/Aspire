using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityToolkit.Aspire.Hosting.Garage.Tests;

public class ResourceCreationTests
{
    [Fact]
    public void GarageResourceGetsAdded()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage");

        using var app = builder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<GarageContainerResource>());

        Assert.Equal("garage", resource.Name);
    }

    [Fact]
    public void GarageResourceHasS3ApiEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        var endpoint = Assert.Single(
            resource.Annotations.OfType<EndpointAnnotation>(),
            a => a.Name == "s3api");

        Assert.Equal(3900, endpoint.TargetPort);
    }

    [Fact]
    public void GarageResourceHasAdminEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        var endpoint = Assert.Single(
            resource.Annotations.OfType<EndpointAnnotation>(),
            a => a.Name == "admin");

        Assert.Equal(3903, endpoint.TargetPort);
    }

    [Fact]
    public void GarageResourceWithCustomPortExposesS3ApiOnThatPort()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage", port: 19000);

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        var endpoint = Assert.Single(
            resource.Annotations.OfType<EndpointAnnotation>(),
            a => a.Name == "s3api");

        Assert.Equal(19000, endpoint.Port);
    }

    [Fact]
    public void GarageResourceWithDataVolumeAddsVolumeAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage").WithDataVolume();

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        // AddGarage also adds a bind-mount for /etc/garage.toml, so filter by target.
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
                            .Single(m => m.Target == "/var/lib/garage");

        Assert.Equal(ContainerMountType.Volume, mount.Type);
    }

    [Fact]
    public void GarageResourceWithDataVolumeUsesCustomVolumeName()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage").WithDataVolume("my-garage-vol");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        // AddGarage also adds a bind-mount for /etc/garage.toml, so filter by target.
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
                            .Single(m => m.Target == "/var/lib/garage");

        Assert.Equal("my-garage-vol", mount.Source);
    }

    [Fact]
    public void GarageResourceWithDataBindMountAddsBindMountAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage").WithDataBindMount("./data/garage");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        // AddGarage also adds a bind-mount for /etc/garage.toml, so filter by target.
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
                            .Single(m => m.Target == "/var/lib/garage");

        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }

    [Fact]
    public void GarageResourceHasProvisioningHealthCheck()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddGarage("garage");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        var result = resource.TryGetAnnotationsOfType<HealthCheckAnnotation>(out var annotations);

        Assert.True(result);
        Assert.Single(annotations!);
    }

    [Fact]
    public async Task GarageResourceWithRegionReflectsRegionInConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddGarage("garage").WithRegion("us-east-1");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        // Allocate an endpoint so the connection string can be resolved.
        var s3Endpoint = resource.Annotations.OfType<EndpointAnnotation>().Single(a => a.Name == "s3api");
        s3Endpoint.AllocatedEndpoint = new AllocatedEndpoint(s3Endpoint, "localhost", 3900);

        var connectionString = await resource.GetConnectionStringAsync();

        Assert.NotNull(connectionString);
        Assert.Contains("Region=us-east-1", connectionString);
    }
}
