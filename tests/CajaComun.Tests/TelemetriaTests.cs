using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using CajaComun.Api.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

/// <summary>Métricas web de usuarios reales: ruta anónima, así que solo acepta valores de una lista cerrada.</summary>
public class TelemetriaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("MariaDb:Database", "prueba");
        b.UseSetting("MariaDb:User", "prueba");
        b.UseSetting("MariaDb:Password", "prueba");
        b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
    }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private const string Ruta = "/api/v1/telemetria/web-vitals";

    /// <summary>Envía un vital y devuelve las etiquetas con las que quedó registrado (vacío si se rechazó).</summary>
    private async Task<(HttpStatusCode Estado, Dictionary<string, object?> Etiquetas, double? Valor)> Enviar(object cuerpo)
    {
        Dictionary<string, object?> etiquetas = [];
        double? valor = null;
        using var oyente = new MeterListener();
        oyente.InstrumentPublished = (i, l) => { if (i.Meter.Name == MedidorCaja.Nombre && i.Name == "caja.web.vital") l.EnableMeasurementEvents(i); };
        oyente.SetMeasurementEventCallback<double>((_, v, tags, _) =>
        {
            valor = v;
            foreach (var t in tags) etiquetas[t.Key] = t.Value;
        });
        oyente.Start();

        var r = await _http.PostAsJsonAsync(Ruta, cuerpo);
        return (r.StatusCode, etiquetas, valor);
    }

    [Fact]
    public async Task Un_vital_valido_se_acepta_sin_sesion_y_se_registra_con_sus_etiquetas()
    {
        var (estado, etiquetas, valor) = await Enviar(new { nombre = "LCP", valor = 1234.5, ruta = "/ahorradores", calificacion = "good" });
        Assert.Equal(HttpStatusCode.NoContent, estado);
        Assert.Equal(1234.5, valor);
        Assert.Equal("LCP", etiquetas["metrica"]);
        Assert.Equal("/ahorradores", etiquetas["ruta"]);
        Assert.Equal("good", etiquetas["calificacion"]);
    }

    [Fact]
    public async Task Los_ids_de_la_ruta_se_normalizan_para_no_multiplicar_etiquetas()
    {
        var (_, etiquetas, _) = await Enviar(new { nombre = "CLS", valor = 0.02, ruta = "/ahorradores/184", calificacion = "good" });
        Assert.Equal("/ahorradores/:id", etiquetas["ruta"]);
    }

    [Fact]
    public async Task Una_ruta_inventada_o_una_calificacion_desconocida_no_crean_etiquetas_nuevas()
    {
        var (estado, etiquetas, _) = await Enviar(new { nombre = "FCP", valor = 800, ruta = "/algo/que/invente/alguien", calificacion = "<script>" });
        Assert.Equal(HttpStatusCode.NoContent, estado);
        Assert.Equal("otra", etiquetas["ruta"]);
        Assert.Equal("desconocida", etiquetas["calificacion"]);
    }

    [Theory]
    [InlineData("XYZ", 10.0)]            // métrica fuera de la lista
    [InlineData("LCP", -1.0)]            // negativo
    [InlineData("LCP", 9_000_000.0)]     // absurdo
    public async Task Los_valores_fuera_de_rango_o_de_la_lista_se_rechazan(string nombre, double valor)
    {
        var (estado, etiquetas, registrado) = await Enviar(new { nombre, valor, ruta = "/", calificacion = "good" });
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Null(registrado);
        Assert.Empty(etiquetas);
    }

    [Fact]
    public async Task Una_ruta_absurdamente_larga_se_rechaza()
    {
        var r = await _http.PostAsJsonAsync(Ruta, new { nombre = "LCP", valor = 1, ruta = new string('a', 5_000), calificacion = "good" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
