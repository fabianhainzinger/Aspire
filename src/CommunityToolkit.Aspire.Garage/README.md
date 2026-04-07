# CommunityToolkit.Aspire.Garage

An Aspire client integration that registers an `IAmazonS3` singleton for connecting to a
[Garage](https://garagehq.deuxfleurs.fr/) S3-compatible object storage server.

## Getting started

### Prerequisites

- A running Garage instance. Use the `CommunityToolkit.Aspire.Hosting.Garage` hosting integration
  or point the client at any manually configured Garage endpoint.

### Install the package

```bash
dotnet add package CommunityToolkit.Aspire.Garage
```

## Usage

In your `Program.cs`:

```csharp
builder.AddGarageClient("garage");
```

This resolves the connection string named `"garage"` from configuration (typically injected by the
Aspire AppHost) and registers `IAmazonS3` as a singleton.

For a named / keyed registration:

```csharp
builder.AddKeyedGarageClient("garage");
// Resolved via:
// app.Services.GetRequiredKeyedService<IAmazonS3>("garage")
```

## Configuration

The client reads settings from `Aspire:Garage:Client` (or `Aspire:Garage:Client:{name}` for keyed registrations).

| Property | Description | Default |
|---|---|---|
| `Endpoint` | S3 API URL | *(from connection string)* |
| `AccessKey` | S3 access key ID | *(from connection string)* |
| `SecretKey` | S3 secret access key | *(from connection string)* |
| `Region` | S3 region name | `garage` |
| `DisableHealthChecks` | Disable the `ListBuckets` health check | `false` |
| `HealthCheckTimeout` | Health check timeout | `null` (no timeout) |

### Connection string format

```
Endpoint=http://localhost:3900;AccessKey=ACCESSKEY;SecretKey=SECRETKEY
```

### Manual configuration example

```json
{
  "Aspire": {
    "Garage": {
      "Client": {
        "Endpoint": "http://localhost:3900",
        "AccessKey": "ACCESSKEYID",
        "SecretKey": "SECRETACCESSKEY",
        "Region": "garage"
      }
    }
  }
}
```

## S3 client notes

The registered `IAmazonS3` is configured with:

- `ForcePathStyle = true` — required for Garage (virtual-hosted-style URLs are not supported)
- `AuthenticationRegion` — set to the `Region` property above

## Health checks

A health check is registered that calls `ListBucketsAsync()`. The check returns `Healthy` when the
call succeeds. It is included automatically and can be disabled via `DisableHealthChecks = true`.
