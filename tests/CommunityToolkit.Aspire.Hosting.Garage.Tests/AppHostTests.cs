using System.Net;
using Aspire.Components.Common.Tests;
using CommunityToolkit.Aspire.Testing;

namespace CommunityToolkit.Aspire.Hosting.Garage.Tests;

[RequiresDocker]
public class AppHostTests(AspireIntegrationTestFixture<Projects.CommunityToolkit_Aspire_Hosting_Garage_AppHost> fixture)
    : IClassFixture<AspireIntegrationTestFixture<Projects.CommunityToolkit_Aspire_Hosting_Garage_AppHost>>
{
    [Fact]
    public async Task ResourceStartsAndRespondsOk()
    {
        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("garage")
            .WaitAsync(TimeSpan.FromMinutes(5));

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("apiservice")
            .WaitAsync(TimeSpan.FromMinutes(2));

        var httpClient = fixture.CreateHttpClient("apiservice");
        var response   = await httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
