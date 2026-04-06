# CommunityToolkit.Aspire.Hosting.Garage

Provides extension methods and resource definitions for the Aspire AppHost to support running [Garage](https://garagehq.deuxfleurs.fr/) containers — a lightweight, self-hosted, S3-compatible object storage server.

## Getting Started

### Install the package

In your AppHost project, install the package using the following command:

```dotnetcli
dotnet add package CommunityToolkit.Aspire.Hosting.Garage
```

### Example usage

In the _Program.cs_ file of your AppHost, add a Garage resource and consume the connection using `WithReference`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var garage = builder.AddGarage("garage")
                    .WithDataVolume();

builder.AddProject<Projects.MyService>("myservice")
       .WithReference(garage)
       .WaitFor(garage);

builder.Build().Run();
```

In your consuming service, register the Garage S3 client:

```csharp
builder.AddGarageClient("garage");
```

This registers an `IAmazonS3` singleton that is pre-configured from the connection string.

## Configuration

| Method | Description |
|---|---|
| `AddGarage(name)` | Adds a Garage container resource. |
| `WithDataVolume(name?)` | Persists data in a named Docker volume at `/var/lib/garage`. |
| `WithDataBindMount(source)` | Persists data using a host directory bind-mount. |
| `WithRegion(region)` | Overrides the S3 region name (default: `garage`). |

## Connection String

The connection string has the following format, compatible with the former MinIO integration:

```
Endpoint=http://{host}:{port};AccessKey={accessKeyId};SecretKey={secretAccessKey}
```

## Known Limitations

- Bucket versioning is not supported. Garage always returns `disabled` for versioning requests.
- ACL and Bucket Policy operations return `501 Not Implemented`. Bucket access is managed via Garage's own key/bucket permission model.
- `ForcePathStyle = true` is required in all S3 clients — virtual-hosted-style URLs require additional DNS configuration that is not present in local development.
- Object Lock and server-side encryption (SSE-S3/KMS) are not supported.

## Migrating from CommunityToolkit.Aspire.Hosting.Minio

The Minio integration has been deprecated. Garage is the recommended replacement.

| | Minio (deprecated) | Garage |
|---|---|---|
| Connection string format | `Endpoint=...;AccessKey=...;SecretKey=...` | identical |
| Hosting package | `CommunityToolkit.Aspire.Hosting.Minio` | `CommunityToolkit.Aspire.Hosting.Garage` |
| Client package | `CommunityToolkit.Aspire.Minio.Client` / `IMinioClient` | `CommunityToolkit.Aspire.Garage` / `IAmazonS3` |
| S3 SDK | `Minio` NuGet | `AWSSDK.S3` NuGet |

Replace `AddMinioContainer` with `AddGarage`, update `WithReference` targets, and switch the service SDK from `IMinioClient` to `IAmazonS3`.
