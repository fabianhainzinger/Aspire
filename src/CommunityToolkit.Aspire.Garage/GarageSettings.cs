using System.Data.Common;

namespace CommunityToolkit.Aspire.Garage;

/// <summary>
/// Provides the client configuration settings for connecting to a Garage S3-compatible
/// object storage server using <see cref="Amazon.S3.IAmazonS3"/>.
/// </summary>
public sealed class GarageSettings
{
    private const string ConnectionStringEndpoint  = "Endpoint";
    private const string ConnectionStringAccessKey = "AccessKey";
    private const string ConnectionStringSecretKey = "SecretKey";
    private const string ConnectionStringRegion    = "Region";

    /// <summary>
    /// Gets or sets the S3 endpoint URL (e.g. <c>http://localhost:3900</c>).
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the S3 access key ID.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// Gets or sets the S3 secret access key.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Gets or sets the S3 region name. Defaults to <c>garage</c>.
    /// </summary>
    /// <remarks>
    /// This must match the region set via <c>AddGarage(...).WithRegion(...)</c> on the hosting side.
    /// </remarks>
    public string Region { get; set; } = "garage";

    /// <summary>
    /// Gets or sets a value that indicates whether the Garage health check is disabled.
    /// </summary>
    /// <value>The default value is <see langword="false"/>.</value>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets the timeout for the Garage health check.
    /// </summary>
    /// <value>The default value is <see langword="null"/> (no timeout).</value>
    public TimeSpan? HealthCheckTimeout { get; set; }

    internal void ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        if (builder.TryGetValue(ConnectionStringEndpoint, out var endpoint)
            && Uri.TryCreate(endpoint.ToString(), UriKind.Absolute, out var uri))
        {
            Endpoint = uri;
        }

        if (builder.TryGetValue(ConnectionStringAccessKey, out var accessKey))
        {
            AccessKey = accessKey.ToString();
        }

        if (builder.TryGetValue(ConnectionStringSecretKey, out var secretKey))
        {
            SecretKey = secretKey.ToString();
        }

        if (builder.TryGetValue(ConnectionStringRegion, out var region)
            && region.ToString() is { Length: > 0 } regionStr)
        {
            Region = regionStr;
        }
    }
}
