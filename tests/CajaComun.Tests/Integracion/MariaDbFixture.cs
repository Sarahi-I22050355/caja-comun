using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MariaDb;

namespace CajaComun.Tests.Integracion;

public static class Categorias
{
    /// <summary>Pruebas que levantan un MariaDB real con Testcontainers; necesitan acceso a Docker.</summary>
    public const string Integracion = "Integracion";
}

/// <summary>Un MariaDB desechable por ejecución, con las migraciones aplicadas. Cada prueba arma su propia caja.</summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    private MariaDbContainer? _contenedor;

    public string Host => _contenedor!.Hostname;
    public int Puerto => _contenedor!.GetMappedPublicPort(3306);
    public string Base => "cajacomun_prueba";
    public string Usuario => "caja";
    public string Clave => "clave-de-prueba-1";

    public string Cadena => $"Server={Host};Port={Puerto};Database={Base};User ID={Usuario};Password={Clave};CharSet=utf8mb4;AllowUserVariables=true";

    public async Task InitializeAsync()
    {
        _contenedor = new MariaDbBuilder()
            .WithImage("mariadb:11")
            .WithDatabase(Base).WithUsername(Usuario).WithPassword(Clave)
            .Build();
        await _contenedor.StartAsync();
        await using var db = Crear();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_contenedor is not null) await _contenedor.DisposeAsync();
    }

    public CajaComunDbContext Crear() =>
        new(new DbContextOptionsBuilder<CajaComunDbContext>()
            .UseMySql(Cadena, new MariaDbServerVersion(new Version(11, 4, 0))).Options);
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionMariaDb : ICollectionFixture<MariaDbFixture>
{
    public const string Nombre = "MariaDb";
}

public sealed class UsuarioPrueba(int id, RolUsuario rol = RolUsuario.Administrador) : IUsuarioActual
{
    public bool EstaAutenticado => true;
    public int Id => id;
    public RolUsuario Rol => rol;
}

/// <summary>Una caja nueva con sus periodos y un administrador, para que las pruebas no se pisen entre sí.</summary>
public sealed class Entorno
{
    private static int _consecutivo;

    private readonly MariaDbFixture _fx;
    private int _ahorradores;

    public int CajaId { get; private set; }
    public string Prefijo { get; } = "TST";
    public int AdminId { get; private set; }
    public IReadOnlyList<Periodo> Periodos { get; private set; } = [];

    private Entorno(MariaDbFixture fx) => _fx = fx;

    public CajaComunDbContext Db() => _fx.Crear();

    public static async Task<Entorno> CrearAsync(MariaDbFixture fx, FrecuenciaNomina frecuencia = FrecuenciaNomina.Quincenal)
    {
        var e = new Entorno(fx);
        var n = Interlocked.Increment(ref _consecutivo);
        await using var db = fx.Crear();

        var empresa = new Empresa { Nombre = $"Empresa de prueba {n} {Guid.NewGuid():N}" };
        var caja = new Caja
        {
            Empresa = empresa, Nombre = "Caja de prueba", Prefijo = e.Prefijo, TasaInteresMensual = 1.5m,
            FrecuenciaNomina = frecuencia, InicioCiclo = new DateOnly(2024, 1, 1), FinCiclo = new DateOnly(2025, 12, 31)
        };
        var admin = new Usuario
        {
            Nombre = "Administrador de prueba", Correo = $"admin-{Guid.NewGuid():N}@prueba.mx", HashPassword = "-",
            Rol = RolUsuario.Administrador, AccesoTodasCajas = true
        };
        db.Add(admin);
        db.Add(caja);
        foreach (var anio in new[] { 2024, 2025 })
            foreach (var p in PeriodosCiclo.Generar(frecuencia, anio))
                db.Periodos.Add(new Periodo { Caja = caja, Anio = anio, Numero = p.Numero, FechaInicio = p.Inicio, FechaFin = p.Fin });
        await db.SaveChangesAsync();

        e.CajaId = caja.Id;
        e.AdminId = admin.Id;
        e.Periodos = await db.Periodos.AsNoTracking().Where(p => p.CajaId == caja.Id)
            .OrderBy(p => p.FechaInicio).ToListAsync();
        return e;
    }

    public MariaDbFixture Fixture => _fx;

    public NominaService Nomina(CajaComunDbContext db, int? usuarioId = null, ICacheDatos? cache = null) =>
        new(db, Acceso(db, usuarioId), new UsuarioPrueba(usuarioId ?? AdminId), TimeProvider.System, cache);

    public CicloService Ciclos(CajaComunDbContext db, int? usuarioId = null, ICacheDatos? cache = null) =>
        new(db, Acceso(db, usuarioId), new UsuarioPrueba(usuarioId ?? AdminId), TimeProvider.System,
            [new LayoutSpeiGenerico(), new LayoutSpeiBanorteSimplificado()], cache);

    public AhorradorService Ahorradores(CajaComunDbContext db, int? usuarioId = null) =>
        new(db, new FabricaConexion(_fx.Cadena), Acceso(db, usuarioId), TimeProvider.System);

    public ReporteService Reportes(CajaComunDbContext db, int? usuarioId = null) =>
        new(db, Acceso(db, usuarioId), Ahorradores(db, usuarioId), Ciclos(db, usuarioId), new SinCache());

    public DashboardService Dashboard(CajaComunDbContext db, int? usuarioId = null, ICacheDatos? cache = null) =>
        new(db, Acceso(db, usuarioId), new MovimientoService(new FabricaConexion(_fx.Cadena), Acceso(db, usuarioId)), cache ?? new SinCache());

    /// <summary>Aplica los periodos del ciclo 2024 en orden (índices 0 a 23).</summary>
    public async Task AplicarAnio2024Async(int desde = 0, int hasta = 23)
    {
        for (var i = desde; i <= hasta; i++)
        {
            await using var db = Db();
            await Nomina(db).AplicarPeriodoAsync(Periodos[i].Id);
        }
    }

    public PrestamoService Prestamos(CajaComunDbContext db, int? usuarioId = null, ICacheDatos? cache = null) =>
        new(db, Acceso(db, usuarioId), new UsuarioPrueba(usuarioId ?? AdminId), TimeProvider.System, cache);

    public ImportacionService Importaciones(CajaComunDbContext db, int? usuarioId = null, ICacheDatos? cache = null) =>
        new(db, Acceso(db, usuarioId), new UsuarioPrueba(usuarioId ?? AdminId), TimeProvider.System, cache);

    public AhorroVoluntarioService Voluntario(CajaComunDbContext db, int? usuarioId = null) =>
        new(db, Acceso(db, usuarioId), TimeProvider.System);

    private CajaAcceso Acceso(CajaComunDbContext db, int? usuarioId) => new(db, new UsuarioPrueba(usuarioId ?? AdminId));

    /// <summary>Un ahorrador activo con su cuota desde mediados de 2023 y su ahorro previo como aportación inicial.</summary>
    public async Task<Ahorrador> AhorradorAsync(decimal ordinaria, decimal ahorro = 0m, decimal voluntaria = 0m)
    {
        await using var db = Db();
        var numero = ReglasAhorrador.NumeroEmpleado(Prefijo, Interlocked.Increment(ref _ahorradores));
        var a = new Ahorrador
        {
            CajaId = CajaId, NumEmpleado = numero, Nombre = "Persona", Apellidos = numero, Rfc = "PEMJ900517AB1",
            FechaNacimiento = new DateOnly(1990, 5, 17), FechaIngreso = new DateOnly(2023, 6, 1), SueldoQuincenal = 9_000m,
            Clabe = Clabe.Completar("01218000123456789"), Departamento = "Prueba"
        };
        db.Ahorradores.Add(a);
        db.CuotasAhorro.Add(new CuotaAhorro
        {
            Ahorrador = a, CuotaOrdinaria = ordinaria, CuotaVoluntaria = voluntaria, VoluntarioActivo = voluntaria > 0m,
            VigenteDesde = new DateOnly(2023, 6, 1)
        });
        await db.SaveChangesAsync();
        if (ahorro > 0m)
        {
            db.Movimientos.Add(new Movimiento
            {
                CajaId = CajaId, AhorradorId = a.Id, Fecha = new DateOnly(2023, 12, 31), Tipo = TipoMovimiento.AhorroOrdinario,
                Monto = ahorro, Referencia = "INICIAL"
            });
            await db.SaveChangesAsync();
        }
        return a;
    }

    /// <summary>
    /// Préstamo vigente con su amortización y su desembolso. La primera cuota cae en el periodo de índice
    /// <paramref name="primerPeriodo"/> (0 = el primero de la caja).
    /// </summary>
    public async Task<Prestamo> PrestamoVigenteAsync(
        Ahorrador a, decimal monto, int plazo, int primerPeriodo = 0, int? avalId = null, decimal excedenteAval = 0m)
    {
        await using var db = Db();
        var p = new Prestamo
        {
            Folio = $"T{Guid.NewGuid():N}"[..20], CajaId = CajaId, AhorradorId = a.Id, Monto = monto, PlazoQuincenas = plazo,
            TasaMensual = 1.5m, Garantia = avalId is null ? GarantiaPrestamo.Ahorro80 : GarantiaPrestamo.PagareAval,
            AvalAhorradorId = avalId, ExcedenteAval = excedenteAval, Estado = EstadoPrestamo.Vigente,
            FechaSolicitud = new DateOnly(2023, 12, 1), FechaAutorizacion = new DateOnly(2023, 12, 10), AutorizadoPor = AdminId
        };
        foreach (var c in CalculadoraAmortizacion.Generar(monto, plazo, 1.5m))
        {
            var periodo = Periodos[primerPeriodo + c.Numero - 1];
            p.Amortizacion.Add(new PrestamoAmortizacion
            {
                Numero = c.Numero, PeriodoId = periodo.Id, Fecha = periodo.FechaFin, Capital = c.Capital, Interes = c.Interes,
                Total = c.Total, SaldoDespues = c.SaldoFinal, Estado = EstadoAmortizacion.Programado
            });
        }
        db.Prestamos.Add(p);
        await db.SaveChangesAsync();
        db.Movimientos.Add(new Movimiento
        {
            CajaId = CajaId, AhorradorId = a.Id, PrestamoId = p.Id, Fecha = new DateOnly(2023, 12, 31),
            Tipo = TipoMovimiento.DesembolsoPrestamo, Monto = monto, Referencia = p.Folio
        });
        await db.SaveChangesAsync();
        return p;
    }

    public async Task IncidenciaAsync(Ahorrador a, int indicePeriodo, TipoIncidencia tipo)
    {
        await using var db = Db();
        db.Incidencias.Add(new Incidencia { AhorradorId = a.Id, PeriodoId = Periodos[indicePeriodo].Id, Tipo = tipo });
        await db.SaveChangesAsync();
    }

    public async Task<List<Movimiento>> MovimientosAsync(int indicePeriodo)
    {
        await using var db = Db();
        var id = Periodos[indicePeriodo].Id;
        return await db.Movimientos.AsNoTracking().Where(m => m.PeriodoId == id).ToListAsync();
    }
}
