using Amazon.Runtime;
using Amazon.S3;
using Aspire;
using CommunityToolkit.Aspire.Garage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering Garage S3-related services in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class AspireGarageExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Garage:Client";

    /// <summary>
    /// Registers <see cref="IAmazonS3"/> as a singleton in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to read config from and add services to.</param>
    /// <param name="connectionName">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">
    /// An optional delegate that can be used for customizing the <see cref="GarageSettings"/>.
    /// It is invoked after settings are read from configuration.
    /// </param>
    public static void AddGarageClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<GarageSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        AddGarageClient(builder, DefaultConfigSectionName, configureSettings, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers <see cref="IAmazonS3"/> as a keyed singleton for the given <paramref name="name"/>
    /// in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to read config from and add services to.</param>
    /// <param name="name">The connection name to use to find a connection string.</param>
    /// <param name="configureSettings">
    /// An optional delegate that can be used for customizing the <see cref="GarageSettings"/>.
    /// It is invoked after settings are read from configuration.
    /// </param>
    public static void AddKeyedGarageClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<GarageSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        AddGarageClient(builder, $"{DefaultConfigSectionName}:{name}", configureSettings, connectionName: name, serviceKey: name);
    }

    private static void AddGarageClient(
        IHostApplicationBuilder builder,
        string configurationSectionName,
        Action<GarageSettings>? configureSettings,
        string connectionName,
        string? serviceKey)
    {
        var settings = new GarageSettings();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
        {
            settings.ParseConnectionString(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton<IAmazonS3>(_ => CreateAmazonS3Client(settings, connectionName, configurationSectionName));
        }
        else
        {
            builder.Services.AddKeyedSingleton<IAmazonS3>(serviceKey,
                (_, _) => CreateAmazonS3Client(settings, connectionName, configurationSectionName));
        }

        if (!settings.DisableHealthChecks)
        {
            var healthCheckName = serviceKey is null ? "Garage" : $"Garage_{connectionName}";

            builder.TryAddHealthCheck(new HealthCheckRegistration(
                healthCheckName,
                _ => new GarageClientHealthCheck(settings),
                failureStatus: default,
                tags: default,
                timeout: settings.HealthCheckTimeout));
        }
    }

    private static AmazonS3Client CreateAmazonS3Client(
        GarageSettings settings, string connectionName, string configurationSectionName)
    {
        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"A Garage S3 client could not be configured. Ensure valid connection information was provided in " +
                $"'ConnectionStrings:{connectionName}' or that '{nameof(GarageSettings.Endpoint)}' is set " +
                $"in the '{configurationSectionName}' configuration section.");
        }

        var credentials = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL          = settings.Endpoint.ToString(),
            ForcePathStyle      = true,
            AuthenticationRegion = settings.Region
        };
        return new AmazonS3Client(credentials, config);
    }
}
