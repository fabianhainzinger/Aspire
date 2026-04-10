using CommunityToolkit.Aspire.Testing;
using CommunityToolkit.Aspire.Hosting.Garage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace CommunityToolkit.Aspire.Garage.Tests;

public class GarageContainerFeature : IAsyncLifetime
{
    private const int S3Port    = 3900;
    private const int AdminPort = 3903;

    // 32 bytes = 64 hex chars, required by Garage for rpc_secret
    private const string DevRpcSecret = "19f4840a892b4dfce05e8c2f87de8888b9c4ab09c9c00834a6ed5b9b8a43f2b0";
    private const string DevAdminToken = "test-admin-token";

    public IContainer? Container { get; private set; }

    public string GetS3Endpoint()
    {
        if (Container is null)
        {
            throw new InvalidOperationException("The test container was not initialized.");
        }

        return new UriBuilder("http", Container.Hostname, Container.GetMappedPublicPort(S3Port)).ToString();
    }

    public async ValueTask InitializeAsync()
    {
        if (RequiresDockerAttribute.IsSupported)
        {
            // The Garage image is scratch-based (no shell). Write a minimal TOML config
            // to a host temp path and bind-mount it into the container.
            var configPath = WriteGarageConfig();

            Container = new ContainerBuilder()
                .WithImage($"{GarageContainerImageTags.Registry}/{GarageContainerImageTags.Image}:{GarageContainerImageTags.Tag}")
                .WithPortBinding(S3Port, true)
                .WithPortBinding(AdminPort, true)
                .WithEnvironment("GARAGE_RPC_SECRET", DevRpcSecret)
                .WithEnvironment("GARAGE_ADMIN_TOKEN", DevAdminToken)
                .WithBindMount(configPath, "/etc/garage.toml", AccessMode.ReadOnly)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPath("/health")
                        .ForPort(AdminPort)))
                .Build();

            await Container.StartAsync();

            await ProvisionGarageAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.DisposeAsync();
        }
    }

    private static string WriteGarageConfig()
    {
        var configDir = Path.Combine(Path.GetTempPath(), "aspire-garage-tests");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "garage.toml");
        File.WriteAllText(configPath, """
            metadata_dir = "/var/lib/garage/meta"
            data_dir = "/var/lib/garage/data"
            db_engine = "sqlite"
            replication_factor = 1
            rpc_bind_addr = "[::]:3901"
            rpc_public_addr = "127.0.0.1:3901"

            [s3_api]
            api_bind_addr = "[::]:3900"
            s3_region = "garage"

            [admin]
            api_bind_addr = "[::]:3903"
            """);
        return configPath;
    }

    private async Task ProvisionGarageAsync()
    {
        var adminBase = new UriBuilder("http", Container!.Hostname, Container.GetMappedPublicPort(AdminPort)).ToString();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", DevAdminToken);

        // Get cluster status to find the node ID
        var statusResp = await http.GetAsync($"{adminBase}/v2/GetClusterStatus");
        statusResp.EnsureSuccessStatusCode();
        var statusDoc = System.Text.Json.JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
        var nodeId = statusDoc.RootElement.GetProperty("nodes")[0].GetProperty("id").GetString()!;

        // Get current layout version
        var layoutResp = await http.GetAsync($"{adminBase}/v2/GetClusterLayout");
        layoutResp.EnsureSuccessStatusCode();
        var layoutDoc = System.Text.Json.JsonDocument.Parse(await layoutResp.Content.ReadAsStringAsync());
        var version = layoutDoc.RootElement.GetProperty("version").GetInt64();

        // Assign layout role
        var updateBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            roles = new[]
            {
                new { id = nodeId, zone = "dc1", capacity = 1_073_741_824L, tags = Array.Empty<string>() }
            }
        });
        using var updateContent = new System.Net.Http.StringContent(updateBody, System.Text.Encoding.UTF8, "application/json");
        (await http.PostAsync($"{adminBase}/v2/UpdateClusterLayout", updateContent)).EnsureSuccessStatusCode();

        // Apply layout
        var applyBody = System.Text.Json.JsonSerializer.Serialize(new { version = version + 1 });
        using var applyContent = new System.Net.Http.StringContent(applyBody, System.Text.Encoding.UTF8, "application/json");
        (await http.PostAsync($"{adminBase}/v2/ApplyClusterLayout", applyContent)).EnsureSuccessStatusCode();

        // Import test access key
        var keyBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            accessKeyId     = "GK1234567890abcdef123456",
            secretAccessKey = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            name            = "test"
        });
        using var keyContent = new System.Net.Http.StringContent(keyBody, System.Text.Encoding.UTF8, "application/json");
        (await http.PostAsync($"{adminBase}/v2/ImportKey", keyContent)).EnsureSuccessStatusCode();
    }
}
