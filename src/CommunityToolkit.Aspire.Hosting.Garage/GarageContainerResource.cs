#pragma warning disable ASPIREATS001 // AspireExport is experimental

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a Garage S3-compatible object storage container.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="accessKeyId">A parameter that contains the Garage access key ID.</param>
/// <param name="secretAccessKey">A parameter that contains the Garage secret access key.</param>
[AspireExport(ExposeProperties = true)]
public sealed class GarageContainerResource(string name, ParameterResource accessKeyId, ParameterResource secretAccessKey)
    : ContainerResource(name), IResourceWithConnectionString
{
    internal const string S3EndpointName    = "s3api";
    internal const string AdminEndpointName = "admin";
    internal const string DefaultRegion     = "garage";

    /// <summary>
    /// Gets the Garage S3 access key ID.
    /// </summary>
    public ParameterResource AccessKeyId { get; } = accessKeyId;

    /// <summary>
    /// Gets the Garage S3 secret access key.
    /// </summary>
    public ParameterResource SecretAccessKey { get; } = secretAccessKey;

    private EndpointReference? _s3Endpoint;
    private EndpointReference? _adminEndpoint;

    /// <summary>
    /// Gets the S3 API endpoint for the Garage container. This endpoint is used for all S3 operations.
    /// </summary>
    public EndpointReference S3Endpoint => _s3Endpoint ??= new(this, S3EndpointName);

    /// <summary>
    /// Gets the admin API endpoint for the Garage container. Used internally for provisioning.
    /// </summary>
    public EndpointReference AdminEndpoint => _adminEndpoint ??= new(this, AdminEndpointName);

    /// <summary>
    /// Gets the connection string expression for the Garage container.
    /// </summary>
    /// <remarks>
    /// Format: <c>Endpoint=http://{host}:{port};AccessKey={accessKeyId};SecretKey={secretAccessKey}</c>
    /// </remarks>
    public ReferenceExpression ConnectionStringExpression => GetConnectionString();

    /// <summary>
    /// Gets the connection string for the Garage container.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A connection string in the form <c>Endpoint=http://host:port;AccessKey=...;SecretKey=...</c>.
    /// </returns>
    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        if (this.TryGetLastAnnotation<ConnectionStringRedirectAnnotation>(out var connectionStringAnnotation))
        {
            return connectionStringAnnotation.Resource.GetConnectionStringAsync(cancellationToken);
        }

        return ConnectionStringExpression.GetValueAsync(cancellationToken);
    }

    private ReferenceExpression GetConnectionString()
    {
        var builder = new ReferenceExpressionBuilder();

        builder.Append(
            $"Endpoint=http://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)}");
        builder.Append($";AccessKey={AccessKeyId}");
        builder.Append($";SecretKey={SecretAccessKey}");

        return builder.Build();
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host",      ReferenceExpression.Create($"{S3Endpoint.Property(EndpointProperty.Host)}"));
        yield return new("Port",      ReferenceExpression.Create($"{S3Endpoint.Property(EndpointProperty.Port)}"));
        yield return new("AccessKey", ReferenceExpression.Create($"{AccessKeyId}"));
        yield return new("SecretKey", ReferenceExpression.Create($"{SecretAccessKey}"));
        yield return new("Endpoint",  ReferenceExpression.Create(
            $"http://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)}"));
    }
}

#pragma warning restore ASPIREATS001 // AspireExport is experimental
