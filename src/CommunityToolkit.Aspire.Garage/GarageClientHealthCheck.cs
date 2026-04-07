using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CommunityToolkit.Aspire.Garage;

/// <summary>
/// A health check that verifies connectivity to a Garage S3-compatible object storage server
/// by listing buckets via the S3 API.
/// </summary>
internal sealed class GarageClientHealthCheck : IHealthCheck
{
    private readonly GarageSettings _settings;

    internal GarageClientHealthCheck(GarageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }

    private AmazonS3Client CreateClient()
    {
        var credentials = new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL    = _settings.Endpoint?.ToString() ?? throw new InvalidOperationException(
                "A Garage S3 client health check could not be configured. The Endpoint must be provided."),
            ForcePathStyle         = true,
            AuthenticationRegion   = _settings.Region
        };
        return new AmazonS3Client(credentials, config);
    }
}
