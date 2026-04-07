#pragma warning disable ASPIREATS001 // AspireExport is experimental

using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Garage;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding Garage resources to an <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
public static class GarageBuilderExtensions
{
    // Stable 64-character hex string used as RPC secret for single-node dev use.
    // Garage requires this for inter-node communication; in a single-node setup any valid 32-byte value works.
    private const string DevRpcSecret = "19f4840a892b4dfce05e8c2f87de8888b9c4ab09c9c00834a6ed5b9b8a43f2b";

    // Name of the env var that the startup shell script reads to set the S3 region in the config file.
    // Not a native Garage env var — used only by the generated init script.
    internal const string RegionEnvVarName = "GARAGE_INIT_S3_REGION";

    // Shell script injected as the container command.
    // Writes a minimal garage.toml using printf (available on Alpine/BusyBox),
    // then exec's the daemon. GARAGE_RPC_SECRET and GARAGE_ADMIN_TOKEN are
    // read directly by Garage from the environment so they do not appear here.
    // The s3_region is filled from GARAGE_INIT_S3_REGION, defaulting to "garage".
    private const string StartupCommand =
        @"printf 'metadata_dir = ""/var/lib/garage/meta""\ndata_dir = ""/var/lib/garage/data""\ndb_engine = ""sqlite""\nreplication_factor = 1\nrpc_bind_addr = ""[::]:3901""\nrpc_public_addr = ""127.0.0.1:3901""\n\n[s3_api]\napi_bind_addr = ""[::]:3900""\ns3_region = ""%s""\n\n[admin]\napi_bind_addr = ""[::]:3903""\n' ${GARAGE_INIT_S3_REGION:-garage} > /tmp/garage.toml && exec /garage --config /tmp/garage.toml server";

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

        var accessKeyIdParam = accessKeyId?.Resource
            ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-accessKeyId");
        var secretKeyParam = secretAccessKey?.Resource
            ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-secretAccessKey");
        var adminTokenParam =
            ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-adminToken");

        var resource = new GarageContainerResource(name, accessKeyIdParam, secretKeyParam);

        return builder
            .AddResource(resource)
            .WithImage(GarageContainerImageTags.Image, GarageContainerImageTags.Tag)
            .WithImageRegistry(GarageContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: 3900, port: port, name: GarageContainerResource.S3EndpointName)
            .WithHttpEndpoint(targetPort: 3903, name: GarageContainerResource.AdminEndpointName)
            .WithEnvironment("GARAGE_RPC_SECRET", DevRpcSecret)
            .WithEnvironment("GARAGE_ADMIN_TOKEN", $"{adminTokenParam}")
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", StartupCommand);
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

        return builder.WithEnvironment(RegionEnvVarName, region);
    }
}

#pragma warning restore ASPIREATS001 // AspireExport is experimental
