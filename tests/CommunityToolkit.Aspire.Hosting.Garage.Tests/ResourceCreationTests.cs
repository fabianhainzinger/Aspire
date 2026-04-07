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

        var mount = Assert.Single(resource.Annotations.OfType<ContainerMountAnnotation>());

        Assert.Equal("/var/lib/garage", mount.Target);
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

        var mount = Assert.Single(resource.Annotations.OfType<ContainerMountAnnotation>());

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

        var mount = Assert.Single(resource.Annotations.OfType<ContainerMountAnnotation>());

        Assert.Equal("/var/lib/garage", mount.Target);
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }

    [Fact]
    public void GarageResourceWithRegionAddsExtraEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var garageBuilder = builder.AddGarage("garage");

        using var app = builder.Build();

        var resource = app.Services.GetRequiredService<DistributedApplicationModel>()
                          .Resources.OfType<GarageContainerResource>().Single();

        var countBefore = resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Count();

        // WithRegion must add exactly one more env annotation
        garageBuilder.WithRegion("us-east-1");

        var countAfter = resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Count();

        Assert.True(countAfter > countBefore,
            "WithRegion should add an environment annotation for GARAGE_INIT_S3_REGION");
    }
}
