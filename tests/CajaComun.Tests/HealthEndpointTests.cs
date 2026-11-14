using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_responde_200_sin_dependencias()
    {
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
