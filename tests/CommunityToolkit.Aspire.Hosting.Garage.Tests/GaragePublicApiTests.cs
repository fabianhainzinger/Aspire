using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CommunityToolkit.Aspire.Hosting.Garage.Tests;

public class GaragePublicApiTests
{
    // ── AddGarage ──────────────────────────────────────────────────────────

    [Fact]
    public void AddGarageShouldThrowWhenBuilderIsNull()
    {
        IDistributedApplicationBuilder builder = null!;

        var action = () => builder.AddGarage("garage");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(builder), exception.ParamName);
    }

    [Fact]
    public void AddGarageShouldThrowWhenNameIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();

        var action = () => builder.AddGarage(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void AddGarageShouldThrowWhenNameIsEmpty()
    {
        var builder = DistributedApplication.CreateBuilder();

        var action = () => builder.AddGarage(string.Empty);

        var exception = Assert.Throws<ArgumentException>(action);
        Assert.Equal("name", exception.ParamName);
    }

    // ── WithDataVolume / WithDataBindMount ──────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithDataShouldThrowWhenBuilderIsNull(bool useVolume)
    {
        IResourceBuilder<GarageContainerResource> builder = null!;

        Func<IResourceBuilder<GarageContainerResource>>? action = useVolume
            ? () => builder.WithDataVolume()
            : () => builder.WithDataBindMount("./data");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(builder), exception.ParamName);
    }

    [Fact]
    public void WithDataBindMountShouldThrowWhenSourceIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        var garageBuilder = builder.AddGarage("garage");

        var action = () => garageBuilder.WithDataBindMount(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("source", exception.ParamName);
    }

    // ── WithRegion ─────────────────────────────────────────────────────────

    [Fact]
    public void WithRegionShouldThrowWhenBuilderIsNull()
    {
        IResourceBuilder<GarageContainerResource> builder = null!;

        var action = () => builder.WithRegion("us-east-1");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(builder), exception.ParamName);
    }

    [Fact]
    public void WithRegionShouldThrowWhenRegionIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        var garageBuilder = builder.AddGarage("garage");

        var action = () => garageBuilder.WithRegion(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("region", exception.ParamName);
    }

    [Fact]
    public void WithRegionShouldThrowWhenRegionIsEmpty()
    {
        var builder = DistributedApplication.CreateBuilder();
        var garageBuilder = builder.AddGarage("garage");

        var action = () => garageBuilder.WithRegion(string.Empty);

        var exception = Assert.Throws<ArgumentException>(action);
        Assert.Equal("region", exception.ParamName);
    }

    // ── GarageContainerResource ─────────────────────────────────────────────


    [Fact]
    public async Task GarageContainerResourceConnectionStringHasCorrectFormat()
    {
        var builder = DistributedApplication.CreateBuilder();

        var accessKeyParam = builder.AddParameter("accessKeyId", "GKtest123key");
        var secretKeyParam = builder.AddParameter("secretKey",   "GKtestSecret");

        var resource = new GarageContainerResource("garage", accessKeyParam.Resource, secretKeyParam.Resource);

        builder.AddResource(resource)
            .WithHttpEndpoint(targetPort: 3900, name: "s3api")
            .WithEndpoint("s3api", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 3900));

        var connectionString = await resource.GetConnectionStringAsync();

        Assert.NotNull(connectionString);
        Assert.Contains("Endpoint=http://localhost:3900", connectionString);
        Assert.Contains("AccessKey=GKtest123key", connectionString);
        Assert.Contains("SecretKey=GKtestSecret", connectionString);
        Assert.Contains("Region=garage", connectionString);
    }

    [Fact]
    public void GarageContainerResourceExposesCorrectCredentialParameters()
    {
        var builder = DistributedApplication.CreateBuilder();

        var accessKeyParam = builder.AddParameter("accessKeyId", "key1");
        var secretKeyParam = builder.AddParameter("secretKey",   "secret1");

        var resource = new GarageContainerResource("garage", accessKeyParam.Resource, secretKeyParam.Resource);

        Assert.Equal(accessKeyParam.Resource, resource.AccessKeyId);
        Assert.Equal(secretKeyParam.Resource, resource.SecretAccessKey);
    }
}
