using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CajaComun.Application.Comun;
using CajaComun.Application.Consultas;
using CajaComun.Infrastructure.Consultas;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

/// <summary>El SQL de la Parte 2: se lee de los mismos archivos que se entregan en docs/.</summary>
public class CatalogoConsultasTests
{
    private static readonly DateOnly Corte = new(2026, 11, 19);

    /// <summary>
    /// Nombres de las columnas del SELECT final (desde "SELECT c.nombre AS caja" hasta el FROM que le sigue): el alias
    /// si lo hay y, si no, el nombre tras el último punto. Las comas dentro de paréntesis no separan columnas.
    /// </summary>
    private static List<string> Columnas(string sql)
    {
        var inicio = sql.LastIndexOf("SELECT c.nombre AS caja", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "No se encontró el SELECT final.");
        var fin = sql.IndexOf("\n  FROM ", inicio, StringComparison.Ordinal);
        var lista = sql[(inicio + "SELECT ".Length)..fin];

        var items = new List<string>();
        var profundidad = 0;
        var actual = new StringBuilder();
        foreach (var c in lista)
        {
            if (c == '(') profundidad++;
            if (c == ')') profundidad--;
            if (c == ',' && profundidad == 0) { items.Add(actual.ToString()); actual.Clear(); }
            else actual.Append(c);
        }
        items.Add(actual.ToString());

        return items.Select(i =>
        {
            var alias = Regex.Match(i, @"\bAS\s+(\w+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return alias.Success ? alias.Groups[1].Value : Regex.Match(i.Trim(), @"(\w+)$").Groups[1].Value;
        }).ToList();
    }

    [Fact]
    public void Los_dos_archivos_traen_las_cinco_consultas()
    {
        foreach (var archivo in new[] { CatalogoConsultas.ArchivoFederado, CatalogoConsultas.ArchivoDirecto })
            Assert.Equal([1, 2, 3, 4, 5], CatalogoConsultas.Dividir(CatalogoConsultas.Recurso(archivo)).Keys.Order());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void La_consulta_federada_une_los_tres_motores_y_la_directa_solo_usa_MariaDB(int n)
    {
        var def = CatalogoConsultas.Buscar(n)!;
        var federada = CatalogoConsultas.Federada(def, 2024, Corte);
        var directa = CatalogoConsultas.Directa(def, 2024, Corte);
        Assert.Contains("mariadb.caja_comun.", federada);
        Assert.Contains("postgresql.public.", federada);
        Assert.Contains("clickhouse.bravo.", federada);
        Assert.DoesNotContain("postgresql", directa);
        Assert.DoesNotContain("clickhouse", directa);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void La_federada_y_la_directa_devuelven_las_mismas_columnas_en_el_mismo_orden(int n)
    {
        var def = CatalogoConsultas.Buscar(n)!;
        var federada = Columnas(CatalogoConsultas.Federada(def, 2024, Corte));
        var directa = Columnas(CatalogoConsultas.Directa(def, 2024, Corte));
        Assert.NotEmpty(federada);
        Assert.Equal(federada, directa);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Los_parametros_se_sustituyen_todos(int n)
    {
        var def = CatalogoConsultas.Buscar(n)!;
        foreach (var sql in new[] { CatalogoConsultas.Federada(def, 2024, Corte), CatalogoConsultas.Directa(def, 2024, Corte) })
        {
            Assert.DoesNotContain("{{", sql);
            Assert.DoesNotContain(";", sql.TrimEnd());
        }
    }

    [Fact]
    public void El_anio_y_la_fecha_de_corte_entran_como_literales()
    {
        var q4 = CatalogoConsultas.Federada(CatalogoConsultas.Buscar(4)!, 2023, Corte);
        Assert.Contains("DATE '2023-01-01'", q4);
        Assert.Contains("DATE '2024-01-01'", q4);

        var q3 = CatalogoConsultas.Federada(CatalogoConsultas.Buscar(3)!, null, Corte);
        Assert.Contains("DATE '2026-11-19'", q3);
        Assert.Contains("TIMESTAMPDIFF(YEAR, a.fecha_nacimiento, DATE '2026-11-19')", CatalogoConsultas.Directa(CatalogoConsultas.Buscar(3)!, null, Corte));
    }

    [Fact]
    public void Solo_Q4_y_Q5_usan_anio_y_solo_Q3_usa_fecha_de_corte()
    {
        Assert.Equal([4, 5], CatalogoConsultas.Definiciones.Where(d => d.UsaAnio).Select(d => d.Numero));
        Assert.Equal([3], CatalogoConsultas.Definiciones.Where(d => d.UsaFechaCorte).Select(d => d.Numero));
    }
}

public class ComparadorFilasTests
{
    private static readonly string[] Tipos = ["varchar", "decimal(38,2)", "bigint", "date"];

    [Fact]
    public void Un_importe_igual_se_reconoce_venga_como_texto_de_Trino_o_decimal_de_MariaDB()
    {
        var federada = new[] { new object?[] { "Ana", "1234.5", 3L, "2024-05-01" } };
        var directa = new[] { new object?[] { "Ana", 1234.50m, 3, new DateTime(2024, 5, 1) } };
        var (dif, ejemplo) = ComparadorFilas.Comparar(federada, directa, Tipos);
        Assert.Equal(0, dif);
        Assert.Null(ejemplo);
    }

    [Fact]
    public void El_orden_no_importa()
    {
        object?[] a = ["Ana", "1.00", 1L, "2024-01-01"], b = ["Beto", "2.00", 2L, "2024-01-02"];
        Assert.Equal(0, ComparadorFilas.Comparar([a, b], [b, a], Tipos).Diferencias);
    }

    [Fact]
    public void Una_fila_de_mas_de_un_lado_se_cuenta_y_se_describe()
    {
        object?[] a = ["Ana", "1.00", 1L, "2024-01-01"], b = ["Beto", "2.00", 2L, "2024-01-02"];
        var (dif, ejemplo) = ComparadorFilas.Comparar([a, b], [a], Tipos);
        Assert.Equal(1, dif);
        Assert.Contains("solo en la federada", ejemplo);
        Assert.Contains("Beto", ejemplo);

        var (dif2, ejemplo2) = ComparadorFilas.Comparar([a], [a, b], Tipos);
        Assert.Equal(1, dif2);
        Assert.Contains("solo en MariaDB", ejemplo2);
    }

    [Fact]
    public void Un_valor_distinto_son_dos_diferencias_y_las_repeticiones_cuentan()
    {
        object?[] a = ["Ana", "1.00", 1L, "2024-01-01"], cambiada = ["Ana", "1.01", 1L, "2024-01-01"];
        Assert.Equal(2, ComparadorFilas.Comparar([a], [cambiada], Tipos).Diferencias);
        Assert.Equal(1, ComparadorFilas.Comparar([a, a], [a], Tipos).Diferencias);
    }

    [Fact]
    public void Los_nulos_se_comparan_como_nulos()
    {
        object?[] a = ["Ana", null, null, null];
        Assert.Equal(0, ComparadorFilas.Comparar([a], [a], Tipos).Diferencias);
        object?[] b = ["Ana", "0.00", null, null];
        Assert.Equal(2, ComparadorFilas.Comparar([a], [b], Tipos).Diferencias);
    }
}

/// <summary>Protocolo REST de Trino con un servidor simulado.</summary>
public class TrinoClientTests
{
    private sealed class Manejador(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Llamadas;
        public List<(HttpMethod Metodo, string Url, string? Cuerpo)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Llamadas++;
            Peticiones.Add((r.Method, r.RequestUri!.ToString(), r.Content is null ? null : await r.Content.ReadAsStringAsync(ct)));
            return responder(r, Llamadas);
        }
    }

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static TrinoClient Cliente(Manejador m, int timeout = 30) =>
        new(new HttpClient(m), new OpcionesTrino { Url = "http://trino:8080", TimeoutSegundos = timeout });

    private const string Pagina1 = """{"id":"q1","nextUri":"http://trino:8080/v1/statement/executing/q1/1","stats":{"state":"QUEUED"}}""";
    private const string Pagina2 = """
        {"id":"q1","nextUri":"http://trino:8080/v1/statement/executing/q1/2",
         "columns":[{"name":"ahorrador","type":"varchar"},{"name":"monto","type":"decimal(38,2)"},{"name":"prestamos","type":"bigint"},{"name":"nacimiento","type":"date"}],
         "data":[["Ana","1234.50",2,"1980-05-01"]]}
        """;
    private const string Pagina3 = """{"id":"q1","data":[["Beto",null,0,null]],"stats":{"state":"FINISHED"}}""";

    [Fact]
    public async Task Envia_el_SQL_con_POST_y_sigue_los_nextUri_juntando_columnas_y_filas()
    {
        var m = new Manejador((r, n) => Json(n switch { 1 => Pagina1, 2 => Pagina2, _ => Pagina3 }));
        var r = await Cliente(m).EjecutarAsync("SELECT 1");

        Assert.Equal(HttpMethod.Post, m.Peticiones[0].Metodo);
        Assert.EndsWith("/v1/statement", m.Peticiones[0].Url);
        Assert.Equal("SELECT 1", m.Peticiones[0].Cuerpo);
        Assert.All(m.Peticiones.Skip(1), p => Assert.Equal(HttpMethod.Get, p.Metodo));
        Assert.Equal(3, m.Llamadas);
        Assert.Equal(["ahorrador", "monto", "prestamos", "nacimiento"], r.Columnas.Select(c => c.Nombre));
        Assert.Equal(2, r.Filas.Count);
    }

    [Fact]
    public async Task Los_enteros_salen_como_enteros_y_los_importes_conservan_su_texto()
    {
        // Regresión: ?: unificaba long y decimal a decimal, y un conteo de 3 salía como 3.00.
        var m = new Manejador((r, n) => Json(n switch { 1 => Pagina1, 2 => Pagina2, _ => Pagina3 }));
        var r = await Cliente(m).EjecutarAsync("SELECT 1");

        Assert.IsType<long>(r.Filas[0][2]);
        Assert.Equal(2L, r.Filas[0][2]);
        Assert.Equal("1234.50", r.Filas[0][1]);
        Assert.Null(r.Filas[1][1]);
        Assert.Null(r.Filas[1][3]);
    }

    [Fact]
    public async Task Un_error_de_Trino_se_convierte_en_un_error_con_codigo_estable()
    {
        var m = new Manejador((r, n) => Json("""{"id":"q1","error":{"message":"Table 'x' does not exist","errorName":"TABLE_NOT_FOUND"}}"""));
        var e = await Assert.ThrowsAsync<ErrorNegocio>(() => Cliente(m).EjecutarAsync("SELECT * FROM x"));
        Assert.Equal(Codigos.ConsultaFederadaFallo, e.Codigo);
        Assert.Contains("TABLE_NOT_FOUND", e.Message);
    }

    [Fact]
    public async Task Reintenta_los_503_y_termina_bien()
    {
        var m = new Manejador((r, n) => n <= 2
            ? Json("", HttpStatusCode.ServiceUnavailable)
            : Json("""{"id":"q1","columns":[{"name":"x","type":"integer"}],"data":[[1]]}"""));
        var r = await Cliente(m).EjecutarAsync("SELECT 1", reintentar: true);
        Assert.Equal(3, m.Llamadas);
        Assert.Single(r.Filas);
    }

    [Fact]
    public async Task Sin_reintentos_un_503_falla_a_la_primera()
    {
        // Un INSERT no se repite a ciegas: no se sabe qué alcanzó a escribir.
        var m = new Manejador((r, n) => Json("", HttpStatusCode.ServiceUnavailable));
        var e = await Assert.ThrowsAsync<ErrorNegocio>(() => Cliente(m).EjecutarAsync("INSERT INTO t SELECT 1", reintentar: false));
        Assert.Equal(Codigos.FederacionNoDisponible, e.Codigo);
        Assert.Equal(1, m.Llamadas);
    }

    [Fact]
    public async Task Sin_conexion_responde_federacion_no_disponible()
    {
        var m = new Manejador((r, n) => throw new HttpRequestException("connection refused"));
        var e = await Assert.ThrowsAsync<ErrorNegocio>(() => Cliente(m).EjecutarAsync("SELECT 1", reintentar: false));
        Assert.Equal(Codigos.FederacionNoDisponible, e.Codigo);
        Assert.Equal(503, e.Estado);
    }

    [Fact]
    public async Task Una_consulta_que_tarda_mas_del_limite_se_cancela_y_se_avisa_a_Trino()
    {
        var m = new Manejador((r, n) => n == 1 ? Json(Pagina1) : Json(Pagina1));   // nunca termina: siempre trae nextUri
        var cliente = new TrinoClient(new HttpClient(new Lento(m)), new OpcionesTrino { Url = "http://trino:8080", TimeoutSegundos = 1 });
        var e = await Assert.ThrowsAsync<ErrorNegocio>(() => cliente.EjecutarAsync("SELECT 1"));
        Assert.Equal(Codigos.FederacionNoDisponible, e.Codigo);
        Assert.Contains(m.Peticiones, p => p.Metodo == HttpMethod.Delete);
    }

    private sealed class Lento(Manejador interno) : DelegatingHandler(interno)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.Method != HttpMethod.Delete) await Task.Delay(400, ct);
            return await base.SendAsync(r, ct);
        }
    }
}

/// <summary>Las rutas de la Parte 2 exigen sesión.</summary>
public class ApiConsultasDistribuidasSeguridadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("MariaDb:Database", "prueba");
        b.UseSetting("MariaDb:User", "prueba");
        b.UseSetting("MariaDb:Password", "prueba");
        b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
    }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/api/v1/consultas-distribuidas")]
    [InlineData("/api/v1/consultas-distribuidas/1")]
    [InlineData("/api/v1/consultas-distribuidas/1/csv")]
    [InlineData("/api/v1/consultas-distribuidas/1/equivalencia")]
    public async Task Sin_token_responde_401(string ruta)
    {
        var r = await _http.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
