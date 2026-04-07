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
internal sealed class GarageProvisioningHealthCheck : IHealthCheck
{
    // A single static HttpClient is safe here because this check is used in local dev only
    // and there is exactly one instance per Garage resource within an Aspire host process.
    private static readonly HttpClient s_httpClient = new();

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
            // ── Step 1: Basic liveness ─────────────────────────────────────────────
            // GET /health requires no authentication and returns 200 when the daemon
            // can handle requests, or 503 when it cannot.
            using var healthResp = await s_httpClient
                .GetAsync($"{baseUrl}/health", ct)
                .ConfigureAwait(false);

            if (!healthResp.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    $"Garage /health returned {(int)healthResp.StatusCode}.");
            }

            // ── Step 2: Layout check & idempotent assignment ───────────────────────
            using var layoutResp = await SendAsync(
                HttpMethod.Get, $"{baseUrl}/v2/GetClusterLayout", adminToken, body: null, ct)
                .ConfigureAwait(false);

            layoutResp.EnsureSuccessStatusCode();

            using var layoutDoc = await ParseJsonDocumentAsync(layoutResp, ct).ConfigureAwait(false);
            var layoutVersion = layoutDoc.RootElement.GetProperty("version").GetInt64();
            var rolesCount    = layoutDoc.RootElement.GetProperty("roles").GetArrayLength();

            if (rolesCount == 0)
            {
                // No layout has been applied yet — assign the local node and apply.
                using var statusResp = await SendAsync(
                    HttpMethod.Get, $"{baseUrl}/v2/GetClusterStatus", adminToken, body: null, ct)
                    .ConfigureAwait(false);

                statusResp.EnsureSuccessStatusCode();

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

                updateResp.EnsureSuccessStatusCode();

                // Apply the staged layout changes. The version must be exactly currentVersion + 1.
                var applyBody = JsonSerializer.Serialize(new { version = layoutVersion + 1 });

                using var applyResp = await SendAsync(
                    HttpMethod.Post, $"{baseUrl}/v2/ApplyClusterLayout", adminToken, applyBody, ct)
                    .ConfigureAwait(false);

                applyResp.EnsureSuccessStatusCode();
            }

            // ── Step 3: Access key check & idempotent import ──────────────────────
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

                importResp.EnsureSuccessStatusCode();
            }

            return HealthCheckResult.Healthy("Garage is ready and provisioned.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await s_httpClient.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ParseJsonDocumentAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }
}
