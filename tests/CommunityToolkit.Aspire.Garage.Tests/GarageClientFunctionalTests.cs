using Amazon.S3;
using Amazon.S3.Model;
using CommunityToolkit.Aspire.Testing;
using Microsoft.Extensions.Hosting;

namespace CommunityToolkit.Aspire.Garage.Tests;

[RequiresDocker]
public class GarageClientFunctionalTests(GarageContainerFeature containerFeature)
    : IClassFixture<GarageContainerFeature>
{
    [Fact]
    public async Task AddGarageClientRegistersWorkingS3Client()
    {
        var endpoint = containerFeature.GetS3Endpoint();

        var appBuilder = Host.CreateApplicationBuilder();
        appBuilder.Configuration["ConnectionStrings:garage"] =
            $"Endpoint={endpoint};AccessKey=GK1234567890abcdef123456;SecretKey=1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef;Region=garage";

        appBuilder.AddGarageClient("garage");

        using var host = appBuilder.Build();
        await host.StartAsync();

        var s3 = host.Services.GetRequiredService<IAmazonS3>();

        const string bucket = "client-test-bucket";
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });

        var list = await s3.ListBucketsAsync();
        Assert.Contains(list.Buckets, b => b.BucketName == bucket);
    }

    [Fact]
    public async Task AddKeyedGarageClientRegistersWorkingS3Client()
    {
        var endpoint = containerFeature.GetS3Endpoint();

        var appBuilder = Host.CreateApplicationBuilder();
        appBuilder.Configuration["ConnectionStrings:garage"] =
            $"Endpoint={endpoint};AccessKey=GK1234567890abcdef123456;SecretKey=1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef;Region=garage";

        appBuilder.AddKeyedGarageClient("garage");

        using var host = appBuilder.Build();
        await host.StartAsync();

        var s3 = host.Services.GetRequiredKeyedService<IAmazonS3>("garage");
        Assert.NotNull(s3);

        var list = await s3.ListBucketsAsync();
        Assert.NotNull(list);
    }
}
