using Microsoft.Extensions.Hosting;

namespace CommunityToolkit.Aspire.Garage.Tests;

public class GarageClientPublicApiTests
{
    [Fact]
    public void AddGarageClientShouldThrowWhenBuilderIsNull()
    {
        IHostApplicationBuilder builder = null!;

        var action = () => builder.AddGarageClient("garage");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(builder), exception.ParamName);
    }

    [Fact]
    public void AddGarageClientShouldThrowWhenConnectionNameIsNull()
    {
        IHostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

        string connectionName = null!;

        var action = () => builder.AddGarageClient(connectionName);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(connectionName), exception.ParamName);
    }

    [Fact]
    public void AddGarageClientShouldThrowWhenConnectionNameIsEmpty()
    {
        IHostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

        var action = () => builder.AddGarageClient(string.Empty);

        var exception = Assert.Throws<ArgumentException>(action);
        Assert.Equal("connectionName", exception.ParamName);
    }

    [Fact]
    public void AddKeyedGarageClientShouldThrowWhenBuilderIsNull()
    {
        IHostApplicationBuilder builder = null!;

        var action = () => builder.AddKeyedGarageClient("garage");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(builder), exception.ParamName);
    }

    [Fact]
    public void AddKeyedGarageClientShouldThrowWhenNameIsNull()
    {
        IHostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

        string name = null!;

        var action = () => builder.AddKeyedGarageClient(name);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal(nameof(name), exception.ParamName);
    }

    [Fact]
    public void AddKeyedGarageClientShouldThrowWhenNameIsEmpty()
    {
        IHostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

        var action = () => builder.AddKeyedGarageClient(string.Empty);

        var exception = Assert.Throws<ArgumentException>(action);
        Assert.Equal("name", exception.ParamName);
    }
}
