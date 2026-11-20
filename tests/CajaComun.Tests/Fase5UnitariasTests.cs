using System.IO.Compression;
using System.Text;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Trabajos;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Infrastructure.Tareas;
using CajaComun.Infrastructure.Trabajos;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace CajaComun.Tests;

public class ReglasCicloTests
{
    [Theory]
    [InlineData(48_650, 10_000, 12_500)]            // 10,000 / 0.80 = 12,500
    [InlineData(48_650, 0, 0)]                      // sin préstamo no se retiene
    [InlineData(5_000, 10_000, 5_000)]              // nunca más de lo que tiene
    [InlineData(0, 10_000, 0)]
    [InlineData(20_000, 333.33, 416.67)]            // 333.33 / 0.80 = 416.6625 -> al centavo hacia arriba
    public void El_ahorro_retenido_respalda_el_prestamo_al_80_por_ciento(double ahorro, double prestamo, double esperado) =>
        Assert.Equal((decimal)esperado, ReglasCiclo.AhorroRetenido((decimal)ahorro, (decimal)prestamo));

    [Fact]
    public void Lo_retenido_siempre_cubre_el_limite_del_80_por_ciento_del_prestamo()
    {
        foreach (var prestamo in new[] { 1m, 99.99m, 1_234.56m, 22_500m })
        {
            var retenido = ReglasCiclo.AhorroRetenido(1_000_000m, prestamo);
            Assert.True(CapacidadCredito.Limite(retenido) >= prestamo - 0.01m, $"{prestamo} -> {retenido}");
        }
    }

    [Theory]
    [InlineData("2024-12-31", "2025-01-01")]     // martes -> miércoles
    [InlineData("2025-01-03", "2025-01-06")]     // viernes -> lunes
    [InlineData("2025-01-04", "2025-01-06")]     // sábado -> lunes
    [InlineData("2025-01-05", "2025-01-06")]     // domingo -> lunes
    public void La_fecha_de_aplicacion_es_el_siguiente_dia_habil(string cierre, string esperado) =>
        Assert.Equal(DateOnly.Parse(esperado), ReglasCiclo.SiguienteDiaHabil(DateOnly.Parse(cierre)));

    [Fact]
    public void La_dispersion_suma_ahorro_y_rendimiento_o_solo_el_rendimiento()
    {
        Assert.Equal(1_050m, ReglasCiclo.ImporteDispersion(1_000m, 50m, incluirAhorro: true));
        Assert.Equal(50m, ReglasCiclo.ImporteDispersion(1_000m, 50m, incluirAhorro: false));
    }
}

public class LayoutsSpeiTests
{
    private static DispersionSpei Fila(int id, string nombre, decimal importe, string? clabe = null) =>
        new(id, $"GIM-{id:00000}", nombre, clabe ?? Clabe.Completar("01218000123456789"), importe);

    private static readonly DispersionSpei[] Filas =
    [
        Fila(1, "Axel Carlin Guzmán", 48_650.00m),
        Fila(2, "Peña Núñez, Ana-Lucía #5", 1_250.5m),
    ];

    private static readonly DateOnly Aplicacion = new(2025, 1, 2);

    [Fact]
    public void El_csv_generico_normaliza_nombres_y_arma_referencias_numericas_de_10_digitos()
    {
        var a = new LayoutSpeiGenerico().Generar(Filas, 2024, Aplicacion, incluirAhorro: true, "GIM");
        var r = Encoding.UTF8.GetString(a.Contenido).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("clabe,beneficiario,importe,concepto,referencia,fecha_aplicacion", r[0]);
        Assert.Equal("012180001234567899,AXEL CARLIN GUZMAN,48650.00,AHORRO Y RENDIMIENTO 2024,2024000001,2025-01-02", r[1]);
        Assert.Equal("012180001234567899,PENA NUNEZ ANA-LUCIA 5,1250.50,AHORRO Y RENDIMIENTO 2024,2024000002,2025-01-02", r[2]);
        Assert.All(r, l => Assert.Equal(5, l.Count(c => c == ',')));      // ningún nombre rompe las columnas
        Assert.Equal((2, 49_900.50m, "SPEI-GIM-2024.csv"), (a.Registros, a.ImporteTotal, a.NombreArchivo));
    }

    [Fact]
    public void Sin_ahorro_el_concepto_es_solo_el_rendimiento()
    {
        var a = new LayoutSpeiGenerico().Generar(Filas, 2024, Aplicacion, incluirAhorro: false, "GIM");
        Assert.Contains("RENDIMIENTO 2024,", Encoding.UTF8.GetString(a.Contenido));
        Assert.DoesNotContain("AHORRO", Encoding.UTF8.GetString(a.Contenido));
    }

    [Fact]
    public void El_layout_de_banorte_es_texto_con_tabuladores_sin_encabezado_y_cuenta_de_20()
    {
        var a = new LayoutSpeiBanorteSimplificado().Generar(Filas, 2024, Aplicacion, incluirAhorro: true, "GIM");
        var r = Encoding.UTF8.GetString(a.Contenido).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, r.Length);
        var campos = r[0].Split('\t');
        Assert.Equal(["GIM-00001", "00012180001234567899", "48650.00", "2024000001", "AHORRO Y RENDIMIENTO 2024"], campos);
        Assert.All(r, l => Assert.True(l.Split('\t')[1].Length == 20 && l.Split('\t')[4].Length <= 30));
        Assert.Equal("SPEI-GIM-2024-BANORTE.txt", a.NombreArchivo);
    }

    [Fact]
    public void Los_dos_formatos_comparten_la_suma_de_control_porque_dispersan_lo_mismo()
    {
        var csv = new LayoutSpeiGenerico().Generar(Filas, 2024, Aplicacion, true, "GIM");
        var txt = new LayoutSpeiBanorteSimplificado().Generar(Filas, 2024, Aplicacion, true, "GIM");
        Assert.Equal(csv.SumaControl, txt.SumaControl);
        Assert.Equal(64, csv.SumaControl.Length);

        var cambiado = new[] { Filas[0], Filas[1] with { Importe = 1_250.51m } };
        Assert.NotEqual(csv.SumaControl, new LayoutSpeiGenerico().Generar(cambiado, 2024, Aplicacion, true, "GIM").SumaControl);
    }

    [Fact]
    public void Un_renglon_invalido_detiene_el_archivo_y_se_informa_cual_es()
    {
        var malas = new[]
        {
            Fila(1, "Bien", 100m),
            Fila(2, "CLABE mala", 100m, clabe: "012180001234567890"),
            Fila(3, "Importe cero", 0m),
            Fila(4, "Tres decimales", 10.123m),
            Fila(5, "###", 100m),
        };
        var error = Assert.Throws<ErrorNegocio>(() => new LayoutSpeiGenerico().Generar(malas, 2024, Aplicacion, true, "GIM"));
        Assert.Equal(Codigos.SpeiRenglonesInvalidos, error.Codigo);
        var renglones = error.Detalles!["renglones"];
        Assert.Equal(["GIM-00002:CLABE_INVALIDA", "GIM-00003:MONTO_INVALIDO", "GIM-00004:MONTO_INVALIDO", "GIM-00005:NOMBRE_INVALIDO"], renglones);
    }

    [Fact]
    public void Sin_dispersiones_no_se_genera_un_archivo_vacio()
    {
        var error = Assert.Throws<ErrorNegocio>(() => new LayoutSpeiGenerico().Generar([], 2024, Aplicacion, true, "GIM"));
        Assert.Equal(Codigos.SpeiSinDispersiones, error.Codigo);
        Assert.Equal(409, error.Estado);
    }

    [Theory]
    [InlineData("Peña Núñez", "PENA NUNEZ")]
    [InlineData("  José   María / López  ", "JOSE MARIA LOPEZ")]
    [InlineData("O'Brien & Hijos, S.A.", "O BRIEN & HIJOS S.A.")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void El_texto_bancario_se_pasa_a_mayusculas_sin_acentos_ni_signos_raros(string? entrada, string esperado) =>
        Assert.Equal(esperado, TextoBancario.Normalizar(entrada, 40));

    [Fact]
    public void El_texto_bancario_se_recorta_al_largo_permitido() =>
        Assert.Equal("ABCDEFGHIJ", TextoBancario.Normalizar("ABCDEFGHIJ KLMNOP", 10));
}

public class PlanTareaTests
{
    [Theory]
    [InlineData(FrecuenciaTarea.Diaria, "02:00", "0 0 2 * * ?")]
    [InlineData(FrecuenciaTarea.Diaria, "23:45", "0 45 23 * * ?")]
    [InlineData(FrecuenciaTarea.Quincenal, "02:00", "0 0 2 1,16 * ?")]
    [InlineData(FrecuenciaTarea.Mensual, "06:30", "0 30 6 1 * ?")]
    public void La_frecuencia_y_la_hora_se_traducen_a_cron(FrecuenciaTarea frecuencia, string hora, string cron) =>
        Assert.Equal(cron, PlanTarea.Cron(frecuencia, TimeOnly.Parse(hora)));

    [Fact]
    public void La_zona_es_la_de_la_ciudad_de_mexico_sin_horario_de_verano()
    {
        var zona = PlanTarea.Zona();
        Assert.Equal(TimeSpan.FromHours(-6), zona.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(-6), zona.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Una_frecuencia_desconocida_se_rechaza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanTarea.Cron((FrecuenciaTarea)99, new TimeOnly(1, 0)));
}

public class ReportesArchivoTests
{
    private static readonly TablaReporte Tabla = new(
        "Prueba de reporte", "Caja de prueba · importes en MXN", [("Total", "$1,000.00")],
        [new("Nombre", TipoColumna.Texto, 2), new("Importe", TipoColumna.Dinero), new("Participación", TipoColumna.Porcentaje), new("Fecha", TipoColumna.Fecha)],
        [["Ana", 1_000.5m, 0.123456m, new DateOnly(2024, 10, 31)], ["Luis", 20m, 0m, new DateOnly(2024, 1, 5)]],
        ["Total", 1_020.5m, 0.123456m, null]);

    [Fact]
    public void El_pdf_es_un_documento_valido_de_varias_paginas_si_hace_falta()
    {
        var chico = ReportePdf.Generar(Tabla);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(chico, 0, 5));

        var filas = Enumerable.Range(0, 400).Select(i => new object?[] { $"Persona {i}", (decimal)i, 0.001m, new DateOnly(2024, 1, 1) }).ToList();
        var grande = ReportePdf.Generar(Tabla with { Filas = filas });
        Assert.True(grande.Length > chico.Length * 2);
    }

    [Fact]
    public void El_excel_conserva_los_tipos_y_los_formatos()
    {
        using var libro = new XLWorkbook(new MemoryStream(ReporteXlsx.Generar(Tabla)));
        var hoja = libro.Worksheet(1);
        Assert.Equal("Prueba de reporte", hoja.Cell(1, 1).GetString());
        Assert.Equal("Total", hoja.Cell(3, 1).GetString());

        // Encabezado en la fila 5; datos desde la 6.
        Assert.Equal("Importe", hoja.Cell(5, 2).GetString());
        Assert.Equal(1_000.5, hoja.Cell(6, 2).Value.GetNumber());
        Assert.Contains("$", hoja.Cell(6, 2).Style.NumberFormat.Format);
        Assert.True(hoja.Cell(6, 4).Value.IsDateTime);
        Assert.Equal(new DateTime(2024, 10, 31), hoja.Cell(6, 4).Value.GetDateTime());
        Assert.Equal(1_020.5, hoja.Cell(8, 2).Value.GetNumber());
        Assert.True(hoja.Cell(8, 1).Style.Font.Bold);
    }

    [Theory]
    [InlineData(1234.5, TipoColumna.Dinero, "$1,234.50")]
    [InlineData(0.123456, TipoColumna.Porcentaje, "12.3456 %")]
    [InlineData(1234.5, TipoColumna.Decimal, "1,234.50")]
    public void El_formato_de_importes_no_depende_de_la_cultura(double valor, TipoColumna tipo, string esperado)
    {
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(esperado, ReportePdf.Formatear((decimal)valor, tipo));
        }
        finally { Thread.CurrentThread.CurrentCulture = anterior; }
    }

    [Fact]
    public void Los_nombres_de_hoja_de_excel_no_llevan_caracteres_invalidos()
    {
        var tabla = Tabla with { Titulo = "Estado: ingresos/gastos [2024]?" };
        using var libro = new XLWorkbook(new MemoryStream(ReporteXlsx.Generar(tabla)));
        Assert.Equal("Estado ingresosgastos 2024", libro.Worksheet(1).Name);
    }
}

public class ColaTrabajosTests
{
    private sealed class Entorno : IAsyncDisposable
    {
        public ServiceProvider Servicios { get; }
        public ColaTrabajos Cola { get; }
        private readonly ProcesadorTrabajos _procesador;

        public Entorno()
        {
            var s = new ServiceCollection();
            s.AddSingleton<IAlmacenTrabajos, AlmacenTrabajosMemoria>();
            s.AddSingleton(TimeProvider.System);
            s.AddSingleton<ColaTrabajos>();
            s.AddScoped<ContextoTrabajo>();
            s.AddLogging();
            s.AddSingleton<ProcesadorTrabajos>();
            Servicios = s.BuildServiceProvider();
            Cola = Servicios.GetRequiredService<ColaTrabajos>();
            _procesador = Servicios.GetRequiredService<ProcesadorTrabajos>();
            _procesador.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<TrabajoDto> EsperarAsync(string id, int usuario = 7, RolUsuario rol = RolUsuario.Administrador)
        {
            for (var i = 0; i < 200; i++)
            {
                var t = await Cola.ObtenerAsync(id, usuario, rol);
                if (t is { Estado: EstadoTrabajo.Exito or EstadoTrabajo.Error }) return t;
                await Task.Delay(25);
            }
            throw new TimeoutException("El trabajo no terminó.");
        }

        public async ValueTask DisposeAsync()
        {
            await _procesador.StopAsync(CancellationToken.None);
            await Servicios.DisposeAsync();
        }
    }

    [Fact]
    public async Task Un_trabajo_pasa_de_pendiente_a_exito_con_su_resultado_y_archivo()
    {
        await using var e = new Entorno();
        var inicial = await e.Cola.EncolarAsync("PRUEBA", 7, RolUsuario.Administrador, (_, _) =>
            Task.FromResult(new ResultadoTrabajo(new { Total = 3, Tipo = RolUsuario.Capturista },
                new CajaComun.Application.Nomina.ArchivoGenerado([1, 2, 3], "x.bin", "application/octet-stream", 3, 0m, ""))));
        Assert.Equal(EstadoTrabajo.Pendiente, inicial.Estado);

        var fin = await e.EsperarAsync(inicial.Id);
        Assert.Equal(EstadoTrabajo.Exito, fin.Estado);
        Assert.NotNull(fin.Inicio);
        Assert.NotNull(fin.Fin);
        Assert.Equal("x.bin", fin.Archivo);
        // El resultado sale con el mismo formato que la API: camelCase y enums como códigos.
        Assert.Equal("""{"total":3,"tipo":"CAPTURISTA"}""", fin.Resultado!.ToString());
        Assert.Equal([1, 2, 3], (await e.Cola.ArchivoAsync(inicial.Id, 7, RolUsuario.Administrador))!.Contenido);
    }

    [Fact]
    public async Task El_trabajo_corre_con_el_usuario_que_lo_pidio()
    {
        await using var e = new Entorno();
        var t = await e.Cola.EncolarAsync("PRUEBA", 42, RolUsuario.Capturista, (sp, _) =>
        {
            var c = sp.GetRequiredService<ContextoTrabajo>();
            return Task.FromResult(new ResultadoTrabajo(new { c.UsuarioId, c.Rol, c.Activo }));
        });
        var fin = await e.EsperarAsync(t.Id, 42, RolUsuario.Capturista);
        Assert.Equal("""{"usuarioId":42,"rol":"CAPTURISTA","activo":true}""", fin.Resultado!.ToString());
    }

    [Fact]
    public async Task Un_error_de_negocio_conserva_su_codigo_y_uno_inesperado_se_oculta()
    {
        await using var e = new Entorno();
        var negocio = await e.Cola.EncolarAsync("PRUEBA", 7, RolUsuario.Administrador,
            (_, _) => throw ErrorNegocio.Conflicto(Codigos.CicloYaCerrado, "Ya está cerrado."));
        var inesperado = await e.Cola.EncolarAsync("PRUEBA", 7, RolUsuario.Administrador,
            (_, _) => throw new InvalidOperationException("detalle interno con datos sensibles"));

        var a = await e.EsperarAsync(negocio.Id);
        Assert.Equal((EstadoTrabajo.Error, Codigos.CicloYaCerrado, "Ya está cerrado."), (a.Estado, a.CodigoError, a.MensajeError));
        var b = await e.EsperarAsync(inesperado.Id);
        Assert.Equal(Codigos.ErrorInterno, b.CodigoError);
        Assert.DoesNotContain("sensibles", b.MensajeError);
        Assert.Null((await e.Cola.ArchivoAsync(negocio.Id, 7, RolUsuario.Administrador)));
    }

    [Fact]
    public async Task Un_trabajo_solo_lo_ve_quien_lo_pidio_o_un_administrador()
    {
        await using var e = new Entorno();
        var t = await e.Cola.EncolarAsync("PRUEBA", 7, RolUsuario.Capturista, (_, _) => Task.FromResult(new ResultadoTrabajo(null)));
        await e.EsperarAsync(t.Id, 7, RolUsuario.Capturista);

        Assert.NotNull(await e.Cola.ObtenerAsync(t.Id, 7, RolUsuario.Capturista));
        Assert.Null(await e.Cola.ObtenerAsync(t.Id, 8, RolUsuario.Capturista));
        Assert.NotNull(await e.Cola.ObtenerAsync(t.Id, 8, RolUsuario.Administrador));
        Assert.Null(await e.Cola.ObtenerAsync("no-existe", 7, RolUsuario.Administrador));
    }

    [Fact]
    public async Task Varios_trabajos_se_procesan_todos_aunque_se_encolen_juntos()
    {
        await using var e = new Entorno();
        var ids = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var numero = i;
            ids.Add((await e.Cola.EncolarAsync("PRUEBA", 7, RolUsuario.Administrador, async (_, _) =>
            {
                await Task.Delay(10);
                return new ResultadoTrabajo(numero);
            })).Id);
        }

        var fines = await Task.WhenAll(ids.Select(id => e.EsperarAsync(id)));
        Assert.All(fines, f => Assert.Equal(EstadoTrabajo.Exito, f.Estado));
        Assert.Equal(Enumerable.Range(0, 20), fines.Select(f => int.Parse(f.Resultado!.ToString()!)).Order());
    }
}
