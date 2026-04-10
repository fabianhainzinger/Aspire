using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Aspire.Components.Common.Tests;
using Aspire.Hosting;
using Aspire.Hosting.Utils;
using CommunityToolkit.Aspire.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityToolkit.Aspire.Hosting.Garage.Tests;

[RequiresDocker]
public class GarageFunctionalTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task StorageGetsCreatedAndUsable()
    {
        using var builder = TestDistributedApplicationBuilder.Create(testOutputHelper);
        var garage     = builder.AddGarage("garage");
        var s3Endpoint = garage.GetEndpoint("s3api");

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceHealthyAsync(garage.Resource.Name).WaitAsync(TimeSpan.FromMinutes(3));

        var accessKeyId = await garage.Resource.AccessKeyId.GetValueAsync(default);
        var secretKey   = await garage.Resource.SecretAccessKey.GetValueAsync(default);

        using var s3 = CreateS3Client(s3Endpoint.Port, accessKeyId!, secretKey!);
        await VerifyS3Operational(s3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithDataShouldPersistStateBetweenUsages(bool useVolume)
    {
        const string bucket = "persist-test";
        const string key    = "hello.txt";

        string? volumeName    = null;
        string? bindMountPath = null;

        string? savedAccessKeyId = null;
        string? savedSecretKey   = null;

        try
        {
            // ── First run: create data ────────────────────────────────────────
            using var builder1 = TestDistributedApplicationBuilder.Create(testOutputHelper);
            var garage1     = builder1.AddGarage("garage");
            var s3Endpoint1 = garage1.GetEndpoint("s3api");

            if (useVolume)
            {
                volumeName = VolumeNameGenerator.Generate(garage1, nameof(WithDataShouldPersistStateBetweenUsages));
                DockerUtils.AttemptDeleteDockerVolume(volumeName, throwOnFailure: true);
                garage1.WithDataVolume(volumeName);
            }
            else
            {
                bindMountPath = Directory.CreateTempSubdirectory().FullName;
                garage1.WithDataBindMount(bindMountPath);
            }

            using (var app1 = builder1.Build())
            {
                await app1.StartAsync();
                var rns1 = app1.Services.GetRequiredService<ResourceNotificationService>();
                await rns1.WaitForResourceHealthyAsync(garage1.Resource.Name).WaitAsync(TimeSpan.FromMinutes(3));

                savedAccessKeyId = await garage1.Resource.AccessKeyId.GetValueAsync(default);
                savedSecretKey   = await garage1.Resource.SecretAccessKey.GetValueAsync(default);

                try
                {
                    using var s3 = CreateS3Client(s3Endpoint1.Port, savedAccessKeyId!, savedSecretKey!);
                    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
                    using var ms = new MemoryStream("hello garage"u8.ToArray());
                    await s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = bucket, Key = key, InputStream = ms, ContentType = "text/plain",
                        UseChunkEncoding = false
                    });
                }
                finally
                {
                    await app1.StopAsync();
                }
            }

            // ── Second run: verify data persisted ────────────────────────────
            using var builder2 = TestDistributedApplicationBuilder.Create(testOutputHelper);
            var garage2     = builder2.AddGarage("garage");
            var s3Endpoint2 = garage2.GetEndpoint("s3api");

            if (useVolume)
            {
                garage2.WithDataVolume(volumeName);
            }
            else
            {
                garage2.WithDataBindMount(bindMountPath!);
            }

            using (var app2 = builder2.Build())
            {
                await app2.StartAsync();
                var rns2 = app2.Services.GetRequiredService<ResourceNotificationService>();
                await rns2.WaitForResourceHealthyAsync(garage2.Resource.Name).WaitAsync(TimeSpan.FromMinutes(3));

                try
                {
                    // Use the same credentials from the first run to access the persisted data.
                    // The key is already present in the persisted data store.
                    using var s3 = CreateS3Client(s3Endpoint2.Port, savedAccessKeyId!, savedSecretKey!);
                    var meta = await s3.GetObjectMetadataAsync(bucket, key);
                    Assert.Equal("text/plain", meta.Headers.ContentType);
                }
                finally
                {
                    await app2.StopAsync();
                }
            }
        }
        finally
        {
            if (volumeName is not null)
            {
                DockerUtils.AttemptDeleteDockerVolume(volumeName);
            }

            if (bindMountPath is not null)
            {
                try { Directory.Delete(bindMountPath, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static IAmazonS3 CreateS3Client(int port, string accessKeyId, string secretKey, string region = "garage")
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(accessKeyId, secretKey),
            new AmazonS3Config
            {
                ServiceURL           = $"http://localhost:{port}",
                ForcePathStyle       = true,
                AuthenticationRegion = region
            });
    }

    private static async Task VerifyS3Operational(IAmazonS3 s3)
    {
        const string bucket = "test-bucket";
        const string key    = "test-object";

        await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });

        var list = await s3.ListBucketsAsync();
        Assert.Contains(list.Buckets, b => b.BucketName == bucket);

        using var ms = new MemoryStream("hello garage"u8.ToArray());
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName       = bucket,
            Key              = key,
            UseChunkEncoding = false,
            InputStream = ms,
            ContentType = "text/plain"
        });

        var meta = await s3.GetObjectMetadataAsync(bucket, key);
        Assert.Equal("text/plain", meta.Headers.ContentType);
    }
}
