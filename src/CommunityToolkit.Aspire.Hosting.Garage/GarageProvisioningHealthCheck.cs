using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CommunityToolkit.Aspire.Hosting.Garage;

/// <summary>
/// A health check that performs idempotent Garage provisioning via the Admin REST API:
/// cluster layout assignment and S3 access key import. Returns <see cref="HealthCheckResult.Healthy"/>
/// only when both provisioning steps have been confirmed, ensuring that any resource depending
/// on the Garage container via <c>WaitFor</c> does not start until S3 is fully operational.
/// </summary>
internal sealed class GarageProvisioningHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly EndpointReference _adminEndpoint;
    private readonly Func<string?> _adminToken;
    private readonly Func<string?> _accessKeyId;
    private readonly Func<string?> _secretAccessKey;
    private readonly string _resourceName;

    internal GarageProvisioningHealthCheck(
        EndpointReference adminEndpoint,
        Func<string?> adminToken,
        Func<string?> accessKeyId,
        Func<string?> secretAccessKey,
        string resourceName)
    {
        _adminEndpoint   = adminEndpoint;
        _adminToken      = adminToken;
        _accessKeyId     = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _resourceName    = resourceName;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        var adminToken      = _adminToken();
        var accessKeyId     = _accessKeyId();
        var secretAccessKey = _secretAccessKey();

        // Parameters are populated by the ConnectionStringAvailableEvent handler in AddGarage.
        // Until that event fires, return Unhealthy so Aspire retries.
        if (adminToken is null || accessKeyId is null || secretAccessKey is null)
        {
            return HealthCheckResult.Unhealthy("Waiting for Garage credentials to be resolved.");
        }

        var baseUrl = _adminEndpoint.Url;
        if (string.IsNullOrEmpty(baseUrl))
        {
            return HealthCheckResult.Unhealthy("Garage admin endpoint URL is not yet available.");
        }

        try
        {
            // ── Step 1: Layout check & idempotent assignment ───────────────────────
            // Use the Admin API as the liveness probe: it works even before quorum is
            // established, unlike GET /health which returns 503 until quorum is ready.
            // A connection-refused exception (caught below) means the daemon is not up yet.
            using var layoutResp = await SendAsync(
                HttpMethod.Get, $"{baseUrl}/v2/GetClusterLayout", adminToken, body: null, ct)
                .ConfigureAwait(false);

            if (!layoutResp.IsSuccessStatusCode)
            {
                var errorBody = await layoutResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return HealthCheckResult.Unhealthy($"GetClusterLayout failed ({(int)layoutResp.StatusCode}): {errorBody}");
            }

            using var layoutDoc = await ParseJsonDocumentAsync(layoutResp, ct).ConfigureAwait(false);
            var layoutVersion = layoutDoc.RootElement.GetProperty("version").GetInt64();
            var rolesCount    = layoutDoc.RootElement.GetProperty("roles").GetArrayLength();

            if (rolesCount == 0)
            {
                // No layout has been applied yet — assign the local node and apply.
                using var statusResp = await SendAsync(
                    HttpMethod.Get, $"{baseUrl}/v2/GetClusterStatus", adminToken, body: null, ct)
                    .ConfigureAwait(false);

                if (!statusResp.IsSuccessStatusCode)
                {
                    var errorBody = await statusResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy($"GetClusterStatus failed ({(int)statusResp.StatusCode}): {errorBody}");
                }

                using var statusDoc = await ParseJsonDocumentAsync(statusResp, ct).ConfigureAwait(false);
                var nodes = statusDoc.RootElement.GetProperty("nodes");

                if (nodes.GetArrayLength() == 0)
                {
                    return HealthCheckResult.Unhealthy("Garage has no connected nodes yet.");
                }

                var nodeId = nodes[0].GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Garage node ID was null in GetClusterStatus response.");

                // Assign a 1 GiB storage role to the single node in zone "dc1".
                var updateBody = JsonSerializer.Serialize(new
                {
                    roles = new[]
                    {
                        new
                        {
                            id       = nodeId,
                            zone     = "dc1",
                            capacity = 1_073_741_824L,   // 1 GiB in bytes
                            tags     = Array.Empty<string>()
                        }
                    }
                });

                using var updateResp = await SendAsync(
                    HttpMethod.Post, $"{baseUrl}/v2/UpdateClusterLayout", adminToken, updateBody, ct)
                    .ConfigureAwait(false);

                if (!updateResp.IsSuccessStatusCode)
                {
                    var errorBody = await updateResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy($"UpdateClusterLayout failed ({(int)updateResp.StatusCode}): {errorBody}");
                }

                // Apply the staged layout changes. The version must be exactly currentVersion + 1.
                var applyBody = JsonSerializer.Serialize(new { version = layoutVersion + 1 });

                using var applyResp = await SendAsync(
                    HttpMethod.Post, $"{baseUrl}/v2/ApplyClusterLayout", adminToken, applyBody, ct)
                    .ConfigureAwait(false);

                if (!applyResp.IsSuccessStatusCode)
                {
                    var errorBody = await applyResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy($"ApplyClusterLayout failed ({(int)applyResp.StatusCode}): {errorBody}");
                }

                // Layout was just applied — return Unhealthy so Aspire retries, giving
                // Garage time to converge quorum before we attempt key import and the
                // final /health gate below.
                return HealthCheckResult.Unhealthy("Cluster layout applied, waiting for quorum to stabilize.");
            }

            // ── Step 2: Access key check & idempotent import ──────────────────────
            using var keyCheckResp = await SendAsync(
                HttpMethod.Get,
                $"{baseUrl}/v2/GetKeyInfo?id={Uri.EscapeDataString(accessKeyId)}",
                adminToken, body: null, ct)
                .ConfigureAwait(false);

            if (!keyCheckResp.IsSuccessStatusCode)
            {
                // Key does not exist — import it.
                var importBody = JsonSerializer.Serialize(new
                {
                    accessKeyId     = accessKeyId,
                    secretAccessKey = secretAccessKey,
                    name            = _resourceName
                });

                using var importResp = await SendAsync(
                    HttpMethod.Post, $"{baseUrl}/v2/ImportKey", adminToken, importBody, ct)
                    .ConfigureAwait(false);

                if (!importResp.IsSuccessStatusCode)
                {
                    var errorBody = await importResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy($"ImportKey failed ({(int)importResp.StatusCode}): {errorBody}");
                }

                // Grant the imported key the right to create buckets.
                var updateKeyBody = JsonSerializer.Serialize(new
                {
                    allow = new { createBucket = true }
                });

                using var updateKeyResp = await SendAsync(
                    HttpMethod.Post, $"{baseUrl}/v2/UpdateKey?id={Uri.EscapeDataString(accessKeyId)}",
                    adminToken, updateKeyBody, ct)
                    .ConfigureAwait(false);

                if (!updateKeyResp.IsSuccessStatusCode)
                {
                    var errorBody = await updateKeyResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy($"UpdateKey (allow createBucket) failed ({(int)updateKeyResp.StatusCode}): {errorBody}");
                }
            }

            // ── Step 3: Final quorum gate ──────────────────────────────────────────
            // Only after both provisioning steps succeed do we check quorum health.
            // Garage returns "unavailable" status until the layout has fully converged;
            // returning Unhealthy here causes Aspire to retry automatically.
            using var clusterHealthResp = await SendAsync(
                HttpMethod.Get, $"{baseUrl}/v2/GetClusterHealth", adminToken, body: null, ct)
                .ConfigureAwait(false);

            if (!clusterHealthResp.IsSuccessStatusCode)
            {
                var body = await clusterHealthResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return HealthCheckResult.Unhealthy(
                    $"Garage GetClusterHealth returned {(int)clusterHealthResp.StatusCode}: {body}");
            }

            using var clusterHealthDoc = await ParseJsonDocumentAsync(clusterHealthResp, ct).ConfigureAwait(false);
            var status = clusterHealthDoc.RootElement.GetProperty("status").GetString();
            if (status != "healthy")
            {
                return HealthCheckResult.Unhealthy($"Garage cluster status: {status}");
            }

            return HealthCheckResult.Healthy("Garage is ready and provisioned.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ParseJsonDocumentAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
