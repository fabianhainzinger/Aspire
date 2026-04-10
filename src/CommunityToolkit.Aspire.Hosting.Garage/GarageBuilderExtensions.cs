#pragma warning disable ASPIREATS001 // AspireExport is experimental

using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Garage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Security.Cryptography;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding Garage resources to an <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
public static class GarageBuilderExtensions
{
    // Stable 64-character hex string used as RPC secret for single-node dev use.
    // Garage requires exactly 32 bytes (64 hex chars) for inter-node authentication.
    // In a single-node setup any valid 32-byte value works.
    private const string DevRpcSecret = "5a6e3ab4c9f2d1e8b7a0c5f3d2e6b9a8c4f7d3e1b6a0c8f2d5e9b3a7c1f4d8e2";

    /// <summary>
    /// Adds a Garage container resource to the application model.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">
    /// The name of the resource. Also used as the connection string name when referenced by dependent resources.
    /// </param>
    /// <param name="accessKeyId">
    /// Optional parameter for the S3 access key ID.
    /// If <see langword="null"/> a random value is generated.
    /// </param>
    /// <param name="secretAccessKey">
    /// Optional parameter for the S3 secret access key.
    /// If <see langword="null"/> a random value is generated.
    /// </param>
    /// <param name="port">
    /// Optional host port for the S3 API endpoint.
    /// If <see langword="null"/> a random port is assigned.
    /// </param>
    /// <returns>A reference to the <see cref="IResourceBuilder{GarageContainerResource}"/>.</returns>
    [AspireExport("addGarage", Description = "Adds a Garage S3-compatible object storage resource")]
    public static IResourceBuilder<GarageContainerResource> AddGarage(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? accessKeyId = null,
        IResourceBuilder<ParameterResource>? secretAccessKey = null,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Garage requires key IDs in the format: "GK" + 24 lowercase hex chars (12 random bytes).
        // Secret keys must be exactly 64 lowercase hex chars (32 random bytes).
        // Admin token can be any string but must be stable across the app run (the same value must
        // reach both the container env var and the health-check closure).
        // Pre-generate all three at AddGarage call time so each lambda captures a stable value.
        var autoKeyId     = "GK" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var autoSecretKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var autoAdminToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var accessKeyIdParam = accessKeyId?.Resource
            ?? new ParameterResource($"{name}-accessKeyId", _ => autoKeyId, secret: false);
        var secretKeyParam = secretAccessKey?.Resource
            ?? new ParameterResource($"{name}-secretAccessKey", _ => autoSecretKey, secret: true);
        var adminTokenParam =
            new ParameterResource($"{name}-adminToken", _ => autoAdminToken, secret: true);

        var resource = new GarageContainerResource(name, accessKeyIdParam, secretKeyParam);

        // Write a minimal TOML config to a temp file and bind-mount it into the container.
        // The official Garage image is scratch-based (no shell), so we cannot use a shell
        // startup script. Garage reads /etc/garage.toml by default (GARAGE_RPC_SECRET and
        // GARAGE_ADMIN_TOKEN are passed as env vars and override the corresponding config keys).
        var configPath = WriteGarageConfig(name, GarageContainerResource.DefaultRegion);

        var resourceBuilder = builder
            .AddResource(resource)
            .WithImage(GarageContainerImageTags.Image, GarageContainerImageTags.Tag)
            .WithImageRegistry(GarageContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 3900, port: port, name: GarageContainerResource.S3EndpointName)
            .WithHttpEndpoint(targetPort: 3903, name: GarageContainerResource.AdminEndpointName)
            .WithEnvironment("GARAGE_RPC_SECRET", DevRpcSecret)
            .WithEnvironment("GARAGE_ADMIN_TOKEN", adminTokenParam)
            .WithBindMount(configPath, "/etc/garage.toml", isReadOnly: true);

        // Capture resolved credentials when the connection string becomes available.
        // The health check closures read these fields; null means the event has not fired yet.
        string? resolvedAdminToken = null, resolvedAccessKeyId = null, resolvedSecretKey = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (@event, ct) =>
        {
            resolvedAdminToken  = await ReferenceExpression.Create($"{adminTokenParam}").GetValueAsync(ct);
            resolvedAccessKeyId = await ReferenceExpression.Create($"{resource.AccessKeyId}").GetValueAsync(ct);
            resolvedSecretKey   = await ReferenceExpression.Create($"{resource.SecretAccessKey}").GetValueAsync(ct);
        });

        var adminEndpoint   = resource.GetEndpoint(GarageContainerResource.AdminEndpointName);
        var healthCheckKey  = $"{name}_garage_provisioning";

        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            healthCheckKey,
            _ => new GarageProvisioningHealthCheck(
                adminEndpoint,
                () => resolvedAdminToken,
                () => resolvedAccessKeyId,
                () => resolvedSecretKey,
                name),
            failureStatus: default,
            tags: default,
            timeout: default));

        return resourceBuilder.WithHealthCheck(healthCheckKey);
    }

    /// <summary>
    /// Adds a named Docker volume for the Garage data directory.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">
    /// The name of the volume. Defaults to an auto-generated name based on the application and resource names.
    /// </param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <example>
    /// Add a Garage container with a persisted data volume:
    /// <code lang="csharp">
    /// var garage = builder.AddGarage("garage")
    ///                     .WithDataVolume();
    /// </code>
    /// </example>
    /// </remarks>
    [AspireExport("withDataVolume", Description = "Adds a named volume for the data directory to a Garage container resource")]
    public static IResourceBuilder<GarageContainerResource> WithDataVolume(
        this IResourceBuilder<GarageContainerResource> builder,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), "/var/lib/garage");
    }

    /// <summary>
    /// Adds a bind mount for the Garage data directory.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The source directory on the host to mount into the container.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <example>
    /// Add a Garage container with a host directory bind-mounted for data:
    /// <code lang="csharp">
    /// var garage = builder.AddGarage("garage")
    ///                     .WithDataBindMount("./data/garage");
    /// </code>
    /// </example>
    /// </remarks>
    [AspireExport("withDataBindMount", Description = "Adds a bind mount for the data directory to a Garage container resource")]
    public static IResourceBuilder<GarageContainerResource> WithDataBindMount(
        this IResourceBuilder<GarageContainerResource> builder,
        string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        return builder.WithBindMount(source, "/var/lib/garage");
    }

    /// <summary>
    /// Sets the S3 region name reported by the Garage server.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="region">The S3 region name. Defaults to <c>garage</c> when not specified.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// The region configured here must match the <c>AuthenticationRegion</c> setting of the AWSSDK.S3 client.
    /// </remarks>
    [AspireExport("withRegion", Description = "Sets the S3 region name reported by Garage")]
    public static IResourceBuilder<GarageContainerResource> WithRegion(
        this IResourceBuilder<GarageContainerResource> builder,
        string region)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(region);

        builder.Resource.Region = region;
        // Rewrite the config file so the new region is reflected before the container starts.
        WriteGarageConfig(builder.Resource.Name, region);
        return builder;
    }

    /// <summary>
    /// Writes a minimal <c>garage.toml</c> config file to a host temp path and returns the path.
    /// </summary>
    /// <remarks>
    /// The file is bind-mounted into the container at <c>/etc/garage.toml</c>.
    /// Garage reads this on startup; secrets and the admin token are supplied via environment
    /// variables so they never appear in the on-disk file.
    /// </remarks>
    private static string WriteGarageConfig(string resourceName, string region)
    {
        var configDir = Path.Combine(Path.GetTempPath(), "aspire-garage");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, $"{resourceName}.toml");
        var config = $"""
            metadata_dir = "/var/lib/garage/meta"
            data_dir = "/var/lib/garage/data"
            db_engine = "sqlite"
            replication_factor = 1
            rpc_bind_addr = "[::]:3901"
            rpc_public_addr = "127.0.0.1:3901"

            [s3_api]
            api_bind_addr = "[::]:3900"
            s3_region = "{region}"

            [admin]
            api_bind_addr = "[::]:3903"
            """;
        File.WriteAllText(configPath, config);
        return configPath;
    }
}

#pragma warning restore ASPIREATS001 // AspireExport is experimental
