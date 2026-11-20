using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_responde_200_sin_dependencias()
    {
        // La liveness no abre conexiones; estos valores solo permiten armar la configuración.
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("MariaDb:Database", "prueba");
            b.UseSetting("MariaDb:User", "prueba");
            b.UseSetting("MariaDb:Password", "prueba");
            b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
        }).CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
