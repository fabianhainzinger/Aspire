namespace CommunityToolkit.Aspire.Garage.Tests;

public class GarageConfigurationTests
{
    [Fact]
    public void EndpointIsNullByDefault() =>
        Assert.Null(new GarageSettings().Endpoint);

    [Fact]
    public void AccessKeyIsNullByDefault() =>
        Assert.Null(new GarageSettings().AccessKey);

    [Fact]
    public void SecretKeyIsNullByDefault() =>
        Assert.Null(new GarageSettings().SecretKey);

    [Fact]
    public void RegionDefaultsToGarage() =>
        Assert.Equal("garage", new GarageSettings().Region);

    [Fact]
    public void HealthChecksEnabledByDefault() =>
        Assert.False(new GarageSettings().DisableHealthChecks);

    [Fact]
    public void HealthCheckTimeoutIsNullByDefault() =>
        Assert.Null(new GarageSettings().HealthCheckTimeout);

    [Fact]
    public void ParseConnectionStringPopulatesEndpointAccessKeyAndSecretKey()
    {
        var settings = new GarageSettings();
        settings.ParseConnectionString(
            "Endpoint=http://localhost:3900;AccessKey=TESTACCESSKEY;SecretKey=TESTSECRETKEY");

        Assert.Equal(new Uri("http://localhost:3900"), settings.Endpoint);
        Assert.Equal("TESTACCESSKEY", settings.AccessKey);
        Assert.Equal("TESTSECRETKEY", settings.SecretKey);
    }

    [Fact]
    public void ParseConnectionStringPreservesDefaultRegionWhenNotPresent()
    {
        var settings = new GarageSettings();
        settings.ParseConnectionString("Endpoint=http://localhost:3900;AccessKey=A;SecretKey=S");

        Assert.Equal("garage", settings.Region);
    }

    [Fact]
    public void ParseConnectionStringOverridesRegionWhenPresent()
    {
        var settings = new GarageSettings();
        settings.ParseConnectionString(
            "Endpoint=http://localhost:3900;AccessKey=A;SecretKey=S;Region=us-east-1");

        Assert.Equal("us-east-1", settings.Region);
    }

    [Fact]
    public void ParseConnectionStringIsNoOpForNullOrEmpty()
    {
        var settings = new GarageSettings();
        settings.ParseConnectionString(null);
        Assert.Null(settings.Endpoint);

        settings.ParseConnectionString(string.Empty);
        Assert.Null(settings.Endpoint);
    }
}
