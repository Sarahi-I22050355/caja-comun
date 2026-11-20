using System.Text;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Tests.Integracion;

[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class ImportacionIntegracionTests(MariaDbFixture fx)
{
    private static readonly string[] Columnas =
    [
        "movimiento", "num_empleado", "nombre", "apellidos", "rfc", "fecha_nacimiento", "fecha_ingreso", "sueldo_quincenal",
        "clabe", "departamento", "cuota_ordinaria", "cuota_voluntaria", "voluntario_activo", "fecha_baja"
    ];

    private static Dictionary<string, string> Alta(string numero, string rfc = "PEMJ900517AB1", string cuotaVoluntaria = "") => new()
    {
        ["movimiento"] = "ALTA", ["num_empleado"] = numero, ["nombre"] = "Julia", ["apellidos"] = "Importada", ["rfc"] = rfc,
        ["fecha_nacimiento"] = "1990-05-17", ["fecha_ingreso"] = "2023-10-01", ["sueldo_quincenal"] = "9000",
        ["clabe"] = Clabe.Completar("01218000123456789"), ["departamento"] = "Almacén", ["cuota_ordinaria"] = "450",
        ["cuota_voluntaria"] = cuotaVoluntaria
    };

    private static Dictionary<string, string> Baja(string numero, DateOnly fecha) => new()
    {
        ["movimiento"] = "BAJA", ["num_empleado"] = numero, ["fecha_baja"] = fecha.ToString("yyyy-MM-dd")
    };

    private static MemoryStream Archivo(params Dictionary<string, string>[] filas)
    {
        var sb = new StringBuilder(string.Join('|', Columnas)).Append('\n');
        foreach (var f in filas) sb.AppendLine(string.Join('|', Columnas.Select(c => f.GetValueOrDefault(c, ""))));
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private async Task<VistaPreviaImportacionDto> CargarAsync(Entorno e, params Dictionary<string, string>[] filas)
    {
        await using var db = e.Db();
        await using var archivo = Archivo(filas);
        return await e.Importaciones(db).CargarAsync(e.CajaId, "lote.txt", archivo);
    }

    private async Task<AplicarImportacionResultadoDto> AplicarAsync(Entorno e, int id, bool omitir = false)
    {
        await using var db = e.Db();
        return await e.Importaciones(db).AplicarAsync(id, omitir);
    }

    [Fact]
    public async Task La_vista_previa_valida_cada_renglon_y_no_toca_el_padron()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e,
            Alta("TST-00001"), Alta("TST-00002", cuotaVoluntaria: "150"), Alta("TST-00003", rfc: "MAL"),
            Alta("TST-00001"), Baja("TST-00099", new DateOnly(2024, 1, 5)), Alta("OTR-00001"));

        var r = vista.Importacion.Resumen;
        Assert.Equal((6, 2, 0, 0, 4), (r.Filas, r.Altas, r.Bajas, r.SinCambio, r.Errores));
        Assert.Equal(EstadoImportacion.Previa, vista.Importacion.Estado);
        Assert.Equal(FormatoArchivo.Txt, vista.Importacion.Formato);

        var codigos = vista.Detalle.Items.ToDictionary(d => d.NumeroFila, d => d.Errores.Select(x => x.Codigo).ToArray());
        Assert.Contains("RFC_INVALIDO", codigos[4]);
        Assert.Contains("DUPLICADO_EN_ARCHIVO", codigos[5]);
        Assert.Contains("EMPLEADO_INEXISTENTE", codigos[6]);
        Assert.Contains("NUM_EMPLEADO_PREFIJO", codigos[7]);

        await using var db = e.Db();
        Assert.Equal(0, await db.Ahorradores.CountAsync(a => a.CajaId == e.CajaId));
    }

    [Fact]
    public async Task Aplicar_un_lote_con_errores_falla_sin_cambiar_nada()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e, Alta("TST-00001"), Alta("TST-00002", rfc: "MAL"));

        var error = await Assert.ThrowsAsync<ErrorNegocio>(() => AplicarAsync(e, vista.Importacion.Id));
        Assert.Equal(Codigos.ImportacionConErrores, error.Codigo);

        await using var db = e.Db();
        Assert.Equal(0, await db.Ahorradores.CountAsync(a => a.CajaId == e.CajaId));
        // El lote sigue siendo una vista previa: se puede corregir el archivo o aplicar omitiendo errores.
        Assert.Equal(EstadoImportacion.Previa, (await db.Importaciones.FindAsync(vista.Importacion.Id))!.Estado);
    }

    [Fact]
    public async Task Aplicar_omitiendo_errores_da_de_alta_solo_los_renglones_validos_con_su_cuota()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e, Alta("TST-00001"), Alta("TST-00002", cuotaVoluntaria: "150"), Alta("TST-00003", rfc: "MAL"));

        var r = await AplicarAsync(e, vista.Importacion.Id, omitir: true);
        Assert.Equal((2, 0, 0, 1), (r.AltasAplicadas, r.BajasAplicadas, r.SinCambio, r.Omitidas));

        await using var db = e.Db();
        var altas = await db.Ahorradores.Where(a => a.CajaId == e.CajaId).OrderBy(a => a.NumEmpleado).ToListAsync();
        Assert.Equal(["TST-00001", "TST-00002"], altas.Select(a => a.NumEmpleado));
        var cuota = await db.CuotasAhorro.SingleAsync(c => c.AhorradorId == altas[1].Id);
        Assert.Equal((450m, 150m, true), (cuota.CuotaOrdinaria, cuota.CuotaVoluntaria, cuota.VoluntarioActivo));
        // El ingreso fue en 2023: la cuota rige desde el primer periodo abierto, no desde una fecha ya aplicada.
        Assert.Equal(new DateOnly(2024, 1, 1), cuota.VigenteDesde);
        Assert.Equal(EstadoImportacion.Aplicada, (await db.Importaciones.FindAsync(vista.Importacion.Id))!.Estado);
    }

    [Fact]
    public async Task Un_lote_se_aplica_una_sola_vez_aunque_lleguen_varias_peticiones()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e, Alta("TST-00001"), Alta("TST-00002"));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return (Ok: await AplicarAsync(e, vista.Importacion.Id), Error: (ErrorNegocio?)null); }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.All(resultados.Where(r => r.Error is not null), r => Assert.Equal(Codigos.ImportacionYaProcesada, r.Error!.Codigo));
        await using var db = e.Db();
        Assert.Equal(2, await db.Ahorradores.CountAsync(a => a.CajaId == e.CajaId));
    }

    [Fact]
    public async Task Volver_a_cargar_un_alta_existente_es_sin_cambio_y_un_numero_con_otra_persona_es_conflicto()
    {
        var e = await Entorno.CrearAsync(fx);
        await AplicarAsync(e, (await CargarAsync(e, Alta("TST-00001"))).Importacion.Id);

        var igual = await CargarAsync(e, Alta("TST-00001"));
        Assert.Equal((1, 0, 0), (igual.Importacion.Resumen.SinCambio, igual.Importacion.Resumen.Altas, igual.Importacion.Resumen.Errores));

        var otraPersona = await CargarAsync(e, Alta("TST-00001", rfc: "GAHL850101XX1"));
        Assert.Contains("NUM_EMPLEADO_DUPLICADO", otraPersona.Detalle.Items.Single().Errores.Select(x => x.Codigo));
    }

    [Fact]
    public async Task La_baja_por_archivo_liquida_el_ahorro_y_respeta_las_reglas_de_baja()
    {
        var e = await Entorno.CrearAsync(fx);
        var libre = await e.AhorradorAsync(1_000m, ahorro: 4_000m);
        var deudor = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        await e.PrestamoVigenteAsync(deudor, 6_000m, 4);
        var inicio = e.Periodos[0].FechaInicio;

        var vista = await CargarAsync(e,
            Baja(libre.NumEmpleado, inicio.AddDays(2)),
            Baja(deudor.NumEmpleado, inicio.AddDays(2)),
            Baja(libre.NumEmpleado.Replace("1", "7"), inicio),                       // no existe
            Baja(e.Prefijo + "-00009", new DateOnly(2023, 1, 1)));
        var errores = vista.Detalle.Items.ToDictionary(d => d.NumeroFila, d => d.Errores.Select(x => x.Codigo).ToArray());
        Assert.Empty(errores.GetValueOrDefault(2) ?? []);
        Assert.Contains(Codigos.BajaConPrestamo, errores[3]);
        Assert.Contains("EMPLEADO_INEXISTENTE", errores[4]);

        var r = await AplicarAsync(e, vista.Importacion.Id, omitir: true);
        Assert.Equal((1, 4_000m), (r.BajasAplicadas, r.AhorroLiquidado));
        await using var db = e.Db();
        var guardado = await db.Ahorradores.FindAsync(libre.Id);
        Assert.Equal(EstadoAhorrador.Baja, guardado!.Estado);
        Assert.Equal(inicio.AddDays(2), guardado.FechaBaja);

        // Una baja ya registrada vuelve a salir como sin cambio.
        var repetida = await CargarAsync(e, Baja(libre.NumEmpleado, inicio.AddDays(2)));
        Assert.Equal(1, repetida.Importacion.Resumen.SinCambio);
    }

    [Fact]
    public async Task Una_baja_con_fecha_de_un_periodo_ya_aplicado_es_error()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 4_000m);
        await using (var db = e.Db()) await e.Nomina(db).AplicarPeriodoAsync(e.Periodos[0].Id);

        var vista = await CargarAsync(e, Baja(a.NumEmpleado, e.Periodos[0].FechaInicio.AddDays(1)));
        Assert.Contains(Codigos.BajaFechaInvalida, vista.Detalle.Items.Single().Errores.Select(x => x.Codigo));
    }

    [Fact]
    public async Task Cancelar_deja_el_lote_sin_efecto_y_no_se_puede_aplicar()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e, Alta("TST-00001"));
        await using (var db = e.Db()) await e.Importaciones(db).CancelarAsync(vista.Importacion.Id);

        var error = await Assert.ThrowsAsync<ErrorNegocio>(() => AplicarAsync(e, vista.Importacion.Id));
        Assert.Equal(Codigos.ImportacionYaProcesada, error.Codigo);
        await using var db2 = e.Db();
        Assert.Equal(0, await db2.Ahorradores.CountAsync(a => a.CajaId == e.CajaId));
    }

    [Fact]
    public async Task El_historial_y_el_detalle_se_filtran_y_paginan()
    {
        var e = await Entorno.CrearAsync(fx);
        var vista = await CargarAsync(e, Alta("TST-00001"), Alta("TST-00002", rfc: "MAL"), Alta("TST-00003", rfc: "MAL"));

        await using var db = e.Db();
        var servicio = e.Importaciones(db);
        var errores = await servicio.ObtenerAsync(vista.Importacion.Id,
            new ConsultaDetalleImportacion { Validacion = ResultadoValidacion.Error, PageSize = 1 });
        Assert.Equal(2, errores.Detalle.Total);
        Assert.Single(errores.Detalle.Items);

        var lista = await servicio.ListarAsync(new ConsultaImportaciones { CajaId = e.CajaId });
        Assert.Equal(vista.Importacion.Id, Assert.Single(lista.Items).Id);
        Assert.Equal(2, lista.Items[0].Resumen.Errores);
    }
}
