using System.Net.Http.Json;

namespace CommunityToolkit.Aspire.Hosting.Garage.Web;

/// <summary>
/// Typed HTTP client for the Garage API Service.
/// </summary>
public class GarageApiClient(HttpClient http)
{
    /// <summary>Returns all S3 buckets.</summary>
    public async Task<BucketInfo[]> GetBucketsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<BucketInfo[]>("/buckets", ct) ?? [];

    /// <summary>Returns all objects inside <paramref name="bucket"/>.</summary>
    public async Task<ObjectInfo[]> GetObjectsAsync(string bucket, CancellationToken ct = default)
        => await http.GetFromJsonAsync<ObjectInfo[]>($"/buckets/{Uri.EscapeDataString(bucket)}", ct) ?? [];

    /// <summary>Creates a new bucket.</summary>
    public async Task CreateBucketAsync(string bucket, CancellationToken ct = default)
        => await http.PutAsync($"/buckets/{Uri.EscapeDataString(bucket)}", content: null, ct);

    /// <summary>Deletes a bucket and all its objects.</summary>
    public async Task DeleteBucketAsync(string bucket, CancellationToken ct = default)
        => await http.DeleteAsync($"/buckets/{Uri.EscapeDataString(bucket)}", ct);

    /// <summary>Uploads an object.</summary>
    public async Task UploadObjectAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = new(contentType);
        await http.PutAsync(
            $"/buckets/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(key)}",
            body, ct);
    }

    /// <summary>Downloads an object as a stream.</summary>
    public async Task<Stream> DownloadObjectAsync(string bucket, string key, CancellationToken ct = default)
        => await http.GetStreamAsync(
            $"/buckets/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(key)}", ct);

    /// <summary>Deletes a single object.</summary>
    public async Task DeleteObjectAsync(string bucket, string key, CancellationToken ct = default)
        => await http.DeleteAsync(
            $"/buckets/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(key)}", ct);

    /// <summary>Represents an S3 bucket.</summary>
    public record BucketInfo(string BucketName, DateTime? CreationDate);

    /// <summary>Represents an S3 object.</summary>
    public record ObjectInfo(string Key, long Size, DateTime LastModified);
}
