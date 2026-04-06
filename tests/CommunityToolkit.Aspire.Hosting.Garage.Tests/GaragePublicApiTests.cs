using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CommunityToolkit.Aspire.Hosting.Garage.Tests;

public class GaragePublicApiTests
{
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
