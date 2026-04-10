using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddGarageClient("garage");

var app = builder.Build();

app.MapDefaultEndpoints();

// List all buckets in Garage
app.MapGet("/buckets", async (IAmazonS3 s3) =>
{
    var response = await s3.ListBucketsAsync();
    return response.Buckets.Select(b => new { b.BucketName, b.CreationDate });
});

// Create a bucket
app.MapPut("/buckets/{bucketName}", async (string bucketName, IAmazonS3 s3) =>
{
    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucketName });
    return Results.Ok();
});

// List objects in a bucket
app.MapGet("/buckets/{bucketName}", async (string bucketName, IAmazonS3 s3) =>
{
    var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucketName });
    return response.S3Objects.Select(o => new { o.Key, o.Size, o.LastModified });
});

// Delete a bucket (empties it first)
app.MapDelete("/buckets/{bucketName}", async (string bucketName, IAmazonS3 s3) =>
{
    var list = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucketName });
    foreach (var obj in list.S3Objects)
        await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucketName, Key = obj.Key });
    await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucketName });
    return Results.NoContent();
});

// Upload an object from the request body
app.MapPut("/buckets/{bucketName}/{**objectKey}", async (
    string bucketName, string objectKey, HttpRequest request, IAmazonS3 s3) =>
{
    // Buffer the body: the AWS SDK requires a seekable stream with a known length.
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    ms.Position = 0;

    await s3.PutObjectAsync(new PutObjectRequest
    {
        BucketName       = bucketName,
        Key              = objectKey,
        InputStream      = ms,
        ContentType      = request.ContentType ?? "application/octet-stream",
        UseChunkEncoding = false
    });
    return Results.Ok();
});

// Download an object
app.MapGet("/buckets/{bucketName}/{**objectKey}", async (
    string bucketName, string objectKey, IAmazonS3 s3) =>
{
    var response = await s3.GetObjectAsync(new GetObjectRequest
    {
        BucketName = bucketName,
        Key        = objectKey
    });
    return Results.Stream(response.ResponseStream, response.Headers.ContentType);
});

// Delete an object
app.MapDelete("/buckets/{bucketName}/{**objectKey}", async (
    string bucketName, string objectKey, IAmazonS3 s3) =>
{
    await s3.DeleteObjectAsync(new DeleteObjectRequest
    {
        BucketName = bucketName,
        Key        = objectKey
    });
    return Results.NoContent();
});

app.Run();
