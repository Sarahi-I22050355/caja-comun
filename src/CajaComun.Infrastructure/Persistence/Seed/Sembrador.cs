using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Persistence.Seed;

/// <summary>
/// Siembra tres cajas con el ciclo 2023 completo (cerrado, con distribución y layout SPEI) y el ciclo 2024
/// hasta la quincena Q20. Todo sale de una simulación quincena por quincena con semilla fija, usando las mismas
/// reglas del dominio (amortización, tope de crédito, saldo-días, reparto por mayor residuo).
/// </summary>
public sealed class Sembrador(CajaComunDbContext db, PerfilSeed perfil, Func<string, string> contrasena)
{
    private const int AnioCierre = 2023;
    private const int PeriodosPorCaja = 72;       // 2023, 2024 y 2025 (las cuotas de los préstamos llegan a 2025)
    private const int IdxCierre = 23;             // Q24 de 2023
    private const int IdxUltimoAplicado = 43;     // Q20 de 2024 (31-oct-2024)
    private const int IdxQ1De2024 = 24;
    private static readonly DateOnly InicioHistorial = new(2023, 1, 1);

    private readonly List<CajaSim> _cajas = [];
    private int _adminId;

    public async Task EjecutarAsync(TextWriter log)
    {
        await SembrarCatalogosAsync(log);

        var pendientes = new List<MovTmp>();
        foreach (var caja in _cajas)
        {
            await CrearPeriodosAsync(caja);
            await CrearAhorradoresAsync(caja);
            Simular(caja);
            log.WriteLine($"  {caja.Caja.Nombre}: {caja.Ahorradores.Count} ahorradores, {caja.Movs.Count:N0} movimientos, {caja.Prestamos.Count} préstamos");
        }

        AsignarFolios();
        await GuardarPrestamosAsync();
        await GuardarCiclosAsync(log);
        foreach (var caja in _cajas) pendientes.AddRange(caja.Movs);
        await GuardarMovimientosAsync(pendientes);
        await GuardarIncidenciasAsync();
        await CalcularInteresDevengadoAsync(log);
    }

    // ---------------------------------------------------------------- catálogos

    private async Task SembrarCatalogosAsync(TextWriter log)
    {
        var empresas = new[]
        {
            new Empresa { Nombre = "Grupo Industrial Monclova, S.A. de C.V." },
            new Empresa { Nombre = "Aceros del Norte, S.A. de C.V." },
            new Empresa { Nombre = "Transportes del Bravo, S.A. de C.V." },
        };
        db.Empresas.AddRange(empresas);
        await db.SaveChangesAsync();

        var definiciones = new (Empresa Empresa, string Nombre, string Prefijo, decimal Tasa, int Cantidad)[]
        {
            (empresas[0], "Caja de Ahorro GIMSA", "GIM", 1.50m, perfil.Gimsa),
            (empresas[1], "Caja de Ahorro Aceros del Norte", "ACE", 1.25m, perfil.Aceros),
            (empresas[2], "Caja de Ahorro Transportes del Bravo", "TDB", 1.75m, perfil.Transportes),
        };
        var i = 0;
        foreach (var d in definiciones)
        {
            var caja = new Caja
            {
                EmpresaId = d.Empresa.Id, Nombre = d.Nombre, Prefijo = d.Prefijo, TasaInteresMensual = d.Tasa,
                FrecuenciaNomina = FrecuenciaNomina.Quincenal,
                InicioCiclo = new DateOnly(2024, 1, 1), FinCiclo = new DateOnly(2024, 12, 31),
            };
            db.Cajas.Add(caja);
            _cajas.Add(new CajaSim(caja, d.Cantidad, new Random(20241031 + i++)));
        }
        await db.SaveChangesAsync();

        var admin = NuevoUsuario("Sarahi Reyes Vázquez", "admin@cajacomun.mx", RolUsuario.Administrador, "SEED_PASSWORD_ADMIN", true);
        var mariana = NuevoUsuario("Mariana Treviño Paz", "mariana.trevino@cajacomun.mx", RolUsuario.Capturista, "SEED_PASSWORD_CAPTURISTA_GIMSA", false);
        var luis = NuevoUsuario("Luis Alberto Garza", "luis.garza@cajacomun.mx", RolUsuario.Consulta, "SEED_PASSWORD_CONSULTA_ACEROS", false);
        var ana = NuevoUsuario("Ana Lucía Ruiz", "ana.ruiz@cajacomun.mx", RolUsuario.Capturista, "SEED_PASSWORD_CAPTURISTA_BRAVO", false);
        db.Usuarios.AddRange(admin, mariana, luis, ana);
        await db.SaveChangesAsync();
        db.UsuarioCajas.AddRange(
            new UsuarioCaja { UsuarioId = mariana.Id, CajaId = _cajas[0].Caja.Id },
            new UsuarioCaja { UsuarioId = luis.Id, CajaId = _cajas[1].Caja.Id },
            new UsuarioCaja { UsuarioId = ana.Id, CajaId = _cajas[2].Caja.Id });
        await db.SaveChangesAsync();
        _adminId = admin.Id;

        db.Tareas.Add(new TareaProgramada
        {
            Clave = "calculo_interes", Nombre = "Cálculo de interés devengado",
            Frecuencia = FrecuenciaTarea.Quincenal, Hora = new TimeOnly(2, 0), Activa = true,
        });
        await db.SaveChangesAsync();
        log.WriteLine("Catálogos listos: 3 empresas, 3 cajas, 4 usuarios, 1 tarea.");
    }

    private Usuario NuevoUsuario(string nombre, string correo, RolUsuario rol, string variable, bool todas) => new()
    {
        Nombre = nombre, Correo = correo, Rol = rol, AccesoTodasCajas = todas,
        HashPassword = BCrypt.Net.BCrypt.HashPassword(contrasena(variable), 11),
    };

    // ---------------------------------------------------------------- periodos y ahorradores

    private async Task CrearPeriodosAsync(CajaSim cs)
    {
        for (var idx = 0; idx < PeriodosPorCaja; idx++)
        {
            var anio = AnioCierre + idx / 24;
            var numero = idx % 24 + 1;
            var (inicio, fin) = Quincenas.Rango(anio, numero);
            cs.Periodos.Add(new Periodo
            {
                CajaId = cs.Caja.Id, Anio = anio, Numero = numero, FechaInicio = inicio, FechaFin = fin,
                Estado = idx <= IdxCierre ? EstadoPeriodo.Cerrado
                       : idx <= IdxUltimoAplicado ? EstadoPeriodo.Aplicado : EstadoPeriodo.Abierto,
            });
        }
        db.Periodos.AddRange(cs.Periodos);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private async Task CrearAhorradoresAsync(CajaSim cs)
    {
        var r = cs.Rnd;
        var esGimsa = cs.Caja.Prefijo == "GIM";
        var primero = esGimsa ? perfil.PrimerNumeroGimsa : 1;

        for (var i = 0; i < cs.Cantidad; i++)
        {
            var numero = primero + i;
            var sim = esGimsa && numero == 123 ? CrearAxel(cs, numero) : CrearAhorrador(cs, numero, r);
            cs.Ahorradores.Add(sim);
        }
        db.Ahorradores.AddRange(cs.Ahorradores.Select(s => s.A));
        await db.SaveChangesAsync();

        foreach (var s in cs.Ahorradores) db.CuotasAhorro.AddRange(CuotasDe(s, cs));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static readonly Dictionary<int, (string Nombre, string Apellidos)> Nombrados = new()
    {
        [101] = ("Jesús", "Carreón Moncada"),
        [102] = ("Carmina", "González Treviño"),
        [103] = ("Roberto", "Morales Estrada"),
        [104] = ("Mayra Lizeth", "Villarreal"),
    };

    private Sim CrearAxel(CajaSim cs, int numero)
    {
        var a = new Ahorrador
        {
            CajaId = cs.Caja.Id, NumEmpleado = $"GIM-{numero:D5}", Nombre = "Axel Carlin", Apellidos = "Guzmán",
            Rfc = DatosPersonas.Rfc("Axel Carlin", "Guzmán", new DateOnly(1999, 3, 15), cs.Rnd),
            FechaNacimiento = new DateOnly(1999, 3, 15), FechaIngreso = new DateOnly(2018, 3, 12),
            SueldoQuincenal = 14_500m,
            // La CLABE del enunciado trae un dígito verificador que no cuadra; se conservan los 17 primeros
            // dígitos y se calcula el correcto para que el layout SPEI no lo rechace.
            Clabe = Clabe.Completar("00248070123456789"),
            Departamento = "Logística y Cadena de Suministro",
        };
        return new Sim(a) { EsAxel = true, Ord = 1_400m, Ord2024 = 1_400m, VolDesde = IdxQ1De2024 + 8, Vol = 500m };
    }

    private Sim CrearAhorrador(CajaSim cs, int numero, Random r)
    {
        var nombrado = cs.Caja.Prefijo == "GIM" && Nombrados.TryGetValue(numero, out _);
        var (nombre, apellidos) = nombrado ? Nombrados[numero] : DatosPersonas.NombreAleatorio(r);

        var edad = r.Next(23, 61);
        var nacimiento = new DateOnly(2024, 10, 31).AddYears(-edad).AddDays(-r.Next(0, 365));
        var tardio = !nombrado && r.NextDouble() < 0.06;
        var ingreso = tardio
            ? new DateOnly(2023, 1, 2).AddDays(r.Next(0, 640))
            : new DateOnly(2005, 1, 1).AddDays(r.Next(0, 365 * 18 + 200));
        var sueldo = Redondea(r.Next(7_000, 24_000), 50m);

        var sim = new Sim(new Ahorrador
        {
            CajaId = cs.Caja.Id, NumEmpleado = $"{cs.Caja.Prefijo}-{numero:D5}", Nombre = nombre, Apellidos = apellidos,
            Rfc = DatosPersonas.Rfc(nombre, apellidos, nacimiento, r), FechaNacimiento = nacimiento, FechaIngreso = ingreso,
            SueldoQuincenal = sueldo, Clabe = DatosPersonas.Clabe(r), Departamento = DatosPersonas.Departamento(r),
        });

        sim.Ord = Math.Max(300m, Redondea(sueldo * (decimal)(0.06 + r.NextDouble() * 0.05), 50m));
        sim.Ord2024 = r.NextDouble() < 0.4 ? Redondea(sim.Ord * 1.08m, 50m) : sim.Ord;
        if (tardio)
        {
            sim.Tardio = true;
            sim.PrimerIdx = Enumerable.Range(0, PeriodosPorCaja).First(i => cs.Periodos[i].FechaFin >= ingreso);
        }

        // Ahorro voluntario: una cuarta parte; algunos lo suspenden definitivamente más adelante.
        if (r.NextDouble() < 0.25)
        {
            sim.VolDesde = Math.Min(41, sim.PrimerIdx + r.Next(1, 40));
            sim.Vol = new[] { 100m, 150m, 200m, 250m, 300m, 400m, 500m, 600m, 800m, 1_000m }[r.Next(10)];
            if (r.NextDouble() < 0.2 && sim.VolDesde < 36) sim.VolHasta = sim.VolDesde + r.Next(4, 8);
        }

        // Situaciones especiales (no se les otorgan préstamos para que su historial sea coherente).
        if (!nombrado && !tardio)
        {
            var x = r.NextDouble();
            if (x < 0.05)
            {
                sim.BajaIdx = r.Next(3, 43);
                var p = cs.Periodos[sim.BajaIdx.Value];
                sim.FechaBaja = p.FechaInicio.AddDays(r.Next(0, p.FechaFin.DayNumber - p.FechaInicio.DayNumber + 1));
                sim.Especial = true;
            }
            else if (x < 0.09)
            {
                var tipo = r.Next(3) == 0 ? TipoIncidencia.Incapacidad : TipoIncidencia.Licencia;
                // Algunas siguen vigentes al cierre de datos (Q20 y Q21) para que existan ahorradores en licencia.
                var inicio = r.NextDouble() < 0.4 ? r.Next(41, 44) : r.Next(10, 42);
                var largo = r.Next(1, 4);
                for (var k = inicio; k < inicio + largo && k <= IdxUltimoAplicado + 1; k++) sim.Incid[k] = tipo;
                sim.Especial = true;
            }
            else if (x < 0.12 && sim.VolDesde is { } vd && vd < 38)
            {
                var inicio = r.Next(Math.Max(vd, IdxQ1De2024), 40);
                for (var k = inicio; k < inicio + r.Next(1, 3); k++) sim.Incid[k] = TipoIncidencia.SuspendeVoluntario;
                sim.Especial = true;
            }
        }

        // El estado actual refleja la situación al corte de datos (Q20 y Q21).
        if (sim.BajaIdx is not null) sim.A.Estado = EstadoAhorrador.Baja;
        else if (sim.Incid.Any(i => i.Key >= IdxUltimoAplicado && i.Value is TipoIncidencia.Licencia or TipoIncidencia.Incapacidad))
            sim.A.Estado = EstadoAhorrador.Licencia;
        return sim;
    }

    private static decimal Redondea(decimal valor, decimal multiplo) =>
        Math.Round(valor / multiplo, 0, MidpointRounding.AwayFromZero) * multiplo;

    // Los montos de préstamo con garantía de ahorro nunca deben pasar del disponible, así que se redondea hacia abajo.
    private static decimal RedondeaAbajo(decimal valor, decimal multiplo) => Math.Floor(valor / multiplo) * multiplo;

    private IEnumerable<CuotaAhorro> CuotasDe(Sim s, CajaSim cs)
    {
        var puntos = new SortedSet<int> { s.PrimerIdx };
        if (s.Ord2024 != s.Ord && IdxQ1De2024 > s.PrimerIdx) puntos.Add(IdxQ1De2024);
        if (s.VolDesde is { } d && d > s.PrimerIdx) puntos.Add(d);
        if (s.VolHasta is { } h && h > s.PrimerIdx) puntos.Add(h);

        foreach (var idx in puntos)
        {
            var (ord, vol, activo) = s.Cuota(idx);
            var desde = idx == s.PrimerIdx
                ? (s.Tardio ? s.A.FechaIngreso : InicioHistorial)
                : cs.Periodos[idx].FechaInicio;
            yield return new CuotaAhorro
            {
                AhorradorId = s.A.Id, CuotaOrdinaria = ord, CuotaVoluntaria = vol, VoluntarioActivo = activo, VigenteDesde = desde,
            };
            // Cambio de cuota registrado como incidencia (con el valor nuevo).
            if (idx == IdxQ1De2024 && s.Ord2024 != s.Ord)
                s.IncidenciasCambio.Add(new Incidencia
                {
                    AhorradorId = s.A.Id, PeriodoId = cs.Periodos[idx].Id, Tipo = TipoIncidencia.CambioCuota,
                    Valor = s.Ord2024, Nota = "Ajuste de cuota para el ciclo 2024",
                });
        }
    }

    // ---------------------------------------------------------------- simulación

    private void Simular(CajaSim cs)
    {
        var r = cs.Rnd;
        var probabilidad = 0.0055 * perfil.FactorPrestamos;

        for (var idx = 0; idx <= IdxUltimoAplicado; idx++)
        {
            var periodo = cs.Periodos[idx];
            if (idx == IdxQ1De2024) AportacionInicialAxel(cs);

            foreach (var s in cs.Ahorradores)
            {
                if (s.BajaIdx is { } b)
                {
                    if (idx > b) continue;
                    if (idx == b)
                    {
                        Agregar(cs, s, TipoMovimiento.LiquidacionCiclo, s.Saldo, s.FechaBaja!.Value, null, null, "BAJA");
                        s.Saldo = 0;
                        continue;
                    }
                }
                if (idx < s.PrimerIdx) continue;
                var incidencia = s.Incid.GetValueOrDefault(idx);
                if (s.Incid.ContainsKey(idx) && incidencia is TipoIncidencia.Licencia or TipoIncidencia.Incapacidad) continue;

                var (ord, vol, activo) = s.Cuota(idx);
                if (ord > 0) { Agregar(cs, s, TipoMovimiento.AhorroOrdinario, ord, periodo.FechaFin, periodo, null); s.Saldo += ord; }
                if (activo && vol > 0 && incidencia != TipoIncidencia.SuspendeVoluntario)
                { Agregar(cs, s, TipoMovimiento.AhorroVoluntario, vol, periodo.FechaFin, periodo, null); s.Saldo += vol; }

                AplicarCuotaPrestamo(cs, s, idx, periodo);
            }

            if (idx == IdxCierre) CerrarCiclo(cs);

            // Solicitudes que se autorizan y desembolsan al cierre de esta quincena (primera cuota en la siguiente).
            var ventana = idx is >= 4 and <= 16 || idx is >= 26 and <= 41;
            if (ventana)
                foreach (var s in cs.Ahorradores)
                    if (Elegible(s, idx) && r.NextDouble() < probabilidad)
                        CrearPrestamoAleatorio(cs, s, idx, r);
            if (idx == IdxQ1De2024 + 13) CrearPrestamoAxel(cs, idx);
        }

        CrearSolicitudesAbiertas(cs, r);
    }

    private static bool Elegible(Sim s, int idx) =>
        !s.Especial && !s.EsAxel && s.Prestamo is null && idx >= s.PrimerIdx + 3 && s.Saldo >= 3_000m;

    private void AportacionInicialAxel(CajaSim cs)
    {
        // Verificación del enunciado: el ahorro ordinario al inicio de 2024 es de $14,650.00. El ahorro del 2023
        // se liquidó al cerrar el ciclo, así que se modela como la aportación inicial del nuevo ciclo.
        var axel = cs.Ahorradores.FirstOrDefault(s => s.EsAxel);
        if (axel is null) return;
        Agregar(cs, axel, TipoMovimiento.AhorroOrdinario, 14_650m, new DateOnly(2024, 1, 2), null, null, "APORTACION-INICIAL-2024");
        axel.Saldo += 14_650m;
    }

    private void AplicarCuotaPrestamo(CajaSim cs, Sim s, int idx, Periodo periodo)
    {
        if (s.Prestamo is not { } sp) return;
        var cuota = sp.Cuotas.FirstOrDefault(c => c.PeriodoIdx == idx);
        if (cuota is null) return;

        Agregar(cs, s, TipoMovimiento.AbonoCapital, cuota.Fila.Capital, periodo.FechaFin, periodo, sp);
        if (cuota.Fila.Interes > 0)
        {
            Agregar(cs, s, TipoMovimiento.AbonoInteres, cuota.Fila.Interes, periodo.FechaFin, periodo, sp);
            if (periodo.Anio == AnioCierre) cs.Interes2023 += cuota.Fila.Interes;
        }
        cuota.Fila.Estado = EstadoAmortizacion.Aplicado;
        s.SaldoPrestamo = cuota.Fila.SaldoDespues;
        if (cuota == sp.Cuotas[^1])
        {
            sp.Prestamo.Estado = EstadoPrestamo.Liquidado;
            s.Prestamo = null;
            s.SaldoPrestamo = 0;
        }
    }

    private void CrearPrestamoAleatorio(CajaSim cs, Sim s, int idx, Random r)
    {
        var disponible = CapacidadCredito.DisponibleSinAval(s.Saldo, s.SaldoPrestamo);
        var tope = s.A.SueldoQuincenal * 8m;
        var pagare = r.NextDouble() < 0.12;

        var plazos = new[] { 6, 8, 10, 12, 16, 18 }.Where(p => idx >= IdxQ1De2024 || idx + p <= IdxCierre).ToArray();
        if (plazos.Length == 0) return;
        var plazo = plazos[r.Next(plazos.Length)];

        var monto = Math.Min(tope, RedondeaAbajo(disponible * (decimal)(0.4 + r.NextDouble() * 0.6), 500m));
        Sim? aval = null;
        if (pagare)
        {
            var solicitado = Math.Min(tope, Redondea(disponible * (decimal)(1.15 + r.NextDouble() * 0.45), 500m));
            if (CapacidadCredito.RequiereAval(solicitado, s.Saldo, s.SaldoPrestamo))
            {
                var excedente = CapacidadCredito.ExcedenteAval(solicitado, s.Saldo, s.SaldoPrestamo);
                aval = cs.Ahorradores.FirstOrDefault(c => c != s && !c.Especial && !c.EsAxel && !c.AvalUsado
                    && idx >= c.PrimerIdx && CapacidadCredito.DisponibleSinAval(c.Saldo, c.SaldoPrestamo) >= excedente);
                if (aval is not null) monto = solicitado;
            }
        }
        if (monto < 1_000m) return;
        if (aval is not null) aval.AvalUsado = true;

        var garantia = aval is null ? GarantiaPrestamo.Ahorro80 : GarantiaPrestamo.PagareAval;
        CrearPrestamo(cs, s, monto, plazo, idx, garantia, aval, folio: null);
    }

    private void CrearPrestamoAxel(CajaSim cs, int idx)
    {
        var axel = cs.Ahorradores.FirstOrDefault(s => s.EsAxel);
        if (axel is null) return;
        CrearPrestamo(cs, axel, 22_500m, 18, idx, GarantiaPrestamo.Ahorro80, null, "FOL-2024-089",
            solicitud: new DateOnly(2024, 7, 22), autorizacion: new DateOnly(2024, 7, 26));
    }

    /// <summary>Desembolsa al cierre del periodo idx; la primera cuota cae en el periodo siguiente.</summary>
    private void CrearPrestamo(CajaSim cs, Sim s, decimal monto, int plazo, int idx, GarantiaPrestamo garantia,
        Sim? aval, string? folio, DateOnly? solicitud = null, DateOnly? autorizacion = null)
    {
        var desembolso = cs.Periodos[idx].FechaFin;
        var tasa = cs.Caja.TasaInteresMensual;
        var prestamo = new Prestamo
        {
            Folio = folio ?? "", CajaId = cs.Caja.Id, AhorradorId = s.A.Id, Monto = monto, PlazoQuincenas = plazo,
            TasaMensual = tasa, Garantia = garantia, AvalAhorradorId = aval?.A.Id, Estado = EstadoPrestamo.Vigente,
            ExcedenteAval = aval is null ? 0m : CapacidadCredito.ExcedenteAval(monto, s.Saldo, s.SaldoPrestamo),
            FechaSolicitud = solicitud ?? desembolso.AddDays(-12),
            FechaAutorizacion = autorizacion ?? desembolso.AddDays(-5),
            AutorizadoPor = _adminId,
        };
        var sp = new SimPrestamo(prestamo);
        foreach (var c in CalculadoraAmortizacion.Generar(monto, plazo, tasa))
        {
            var pIdx = idx + c.Numero;
            sp.Cuotas.Add(new CuotaSim(pIdx, new PrestamoAmortizacion
            {
                Numero = c.Numero, PeriodoId = cs.Periodos[pIdx].Id, Fecha = cs.Periodos[pIdx].FechaFin,
                Capital = c.Capital, Interes = c.Interes, Total = c.Total, SaldoDespues = c.SaldoFinal,
                Estado = EstadoAmortizacion.Programado,
            }));
        }
        foreach (var c in sp.Cuotas) prestamo.Amortizacion.Add(c.Fila);

        cs.Prestamos.Add(sp);
        s.Prestamo = sp;
        s.SaldoPrestamo = monto;
        Agregar(cs, s, TipoMovimiento.DesembolsoPrestamo, monto, desembolso, null, sp);
    }

    // Al corte de datos hay solicitudes por resolver y algunas rechazadas (sin movimientos).
    private void CrearSolicitudesAbiertas(CajaSim cs, Random r)
    {
        foreach (var s in cs.Ahorradores)
        {
            if (!Elegible(s, IdxUltimoAplicado)) continue;
            var x = r.NextDouble() / perfil.FactorPrestamos;
            if (x >= 0.03) continue;
            var disponible = CapacidadCredito.DisponibleSinAval(s.Saldo, 0m);
            var monto = Math.Min(s.A.SueldoQuincenal * 8m, RedondeaAbajo(disponible * (decimal)(0.4 + r.NextDouble() * 0.5), 500m));
            if (monto < 1_000m) continue;

            var plazo = new[] { 6, 8, 12, 18 }[r.Next(4)];
            var pendiente = x < 0.02;
            var prestamo = new Prestamo
            {
                Folio = "", CajaId = cs.Caja.Id, AhorradorId = s.A.Id, Monto = monto, PlazoQuincenas = plazo,
                TasaMensual = cs.Caja.TasaInteresMensual, Garantia = GarantiaPrestamo.Ahorro80,
                Estado = pendiente ? EstadoPrestamo.Pendiente : EstadoPrestamo.Rechazado,
                FechaSolicitud = new DateOnly(2024, 10, r.Next(15, 32)),
            };
            var sp = new SimPrestamo(prestamo);
            if (pendiente)
            {
                // Si se autoriza, el desembolso sería en Q21 y la primera cuota en Q22.
                foreach (var c in CalculadoraAmortizacion.Generar(monto, plazo, prestamo.TasaMensual))
                {
                    var pIdx = IdxUltimoAplicado + 1 + c.Numero;
                    prestamo.Amortizacion.Add(new PrestamoAmortizacion
                    {
                        Numero = c.Numero, PeriodoId = cs.Periodos[pIdx].Id, Fecha = cs.Periodos[pIdx].FechaFin,
                        Capital = c.Capital, Interes = c.Interes, Total = c.Total, SaldoDespues = c.SaldoFinal,
                        Estado = EstadoAmortizacion.Proyectado,
                    });
                }
            }
            cs.Prestamos.Add(sp);
        }
    }

    // ---------------------------------------------------------------- cierre del ciclo 2023

    private void CerrarCiclo(CajaSim cs)
    {
        var inicio = new DateOnly(AnioCierre, 1, 1);
        var fin = new DateOnly(AnioCierre, 12, 31);
        var participantes = new List<(Sim Sim, ResultadoSaldoDias Resultado)>();
        foreach (var s in cs.Ahorradores)
        {
            var r = SaldoDias.Calcular(s.Aportes, s.Liquidaciones, inicio, fin);
            if (r.SaldoDias > 0) participantes.Add((s, r));
        }

        var interesTotal = cs.Interes2023;
        var reparto = DistribucionProporcional.Repartir(interesTotal, participantes.Select(p => p.Resultado.SaldoDias).ToList());
        var totalSaldoDias = participantes.Sum(p => p.Resultado.SaldoDias);

        cs.Cierre = new CierreSim(interesTotal);
        for (var i = 0; i < participantes.Count; i++)
        {
            var (s, res) = participantes[i];
            var asignado = reparto[i];
            var liquidado = s.Saldo;      // cero para quien ya se liquidó al darse de baja
            cs.Cierre.Filas.Add(new CicloDistribucion
            {
                AhorradorId = s.A.Id, SaldoDias = res.SaldoDias, SaldoPromedio = res.SaldoPromedio, DiasActivos = res.DiasActivos,
                Participacion = decimal.Round(res.SaldoDias / totalSaldoDias, 6, MidpointRounding.AwayFromZero),
                InteresAsignado = asignado, AhorroLiquidado = liquidado,
            });
            if (asignado > 0) Agregar(cs, s, TipoMovimiento.InteresAnual, asignado, fin, null, null, "CIERRE-2023");
            if (liquidado > 0) Agregar(cs, s, TipoMovimiento.LiquidacionCiclo, liquidado, fin, null, null, "CIERRE-2023");
            s.Saldo = 0;
        }
    }

    // ---------------------------------------------------------------- persistencia

    private void AsignarFolios()
    {
        var todos = _cajas.SelectMany((c, ci) => c.Prestamos.Select(p => (c, ci, p))).ToList();
        foreach (var grupo in todos.Where(t => t.p.Prestamo.Folio == "")
                     .OrderBy(t => t.p.Prestamo.FechaSolicitud).ThenBy(t => t.ci)
                     .GroupBy(t => t.p.Prestamo.FechaSolicitud.Year))
        {
            var n = 0;
            foreach (var t in grupo)
            {
                n++;
                if (grupo.Key == 2024 && n == 89) n++;     // FOL-2024-089 es el préstamo de verificación
                t.p.Prestamo.Folio = $"FOL-{grupo.Key}-{n:D3}";
            }
        }
    }

    private async Task GuardarPrestamosAsync()
    {
        foreach (var cs in _cajas)
        {
            db.Prestamos.AddRange(cs.Prestamos.Select(p => p.Prestamo));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private async Task GuardarCiclosAsync(TextWriter log)
    {
        foreach (var cs in _cajas)
        {
            var cierre = cs.Cierre!;
            var cerrado = new Ciclo
            {
                CajaId = cs.Caja.Id, Anio = AnioCierre, Estado = EstadoCiclo.Cerrado, InteresTotal = cierre.InteresTotal,
                FechaCierre = new DateTime(AnioCierre, 12, 31, 18, 0, 0, DateTimeKind.Utc),
            };
            var abierto = new Ciclo { CajaId = cs.Caja.Id, Anio = AnioCierre + 1, Estado = EstadoCiclo.Abierto, InteresTotal = 0 };
            db.Ciclos.AddRange(cerrado, abierto);
            await db.SaveChangesAsync();

            foreach (var f in cierre.Filas) f.CicloId = cerrado.Id;
            db.CicloDistribuciones.AddRange(cierre.Filas);

            var dispersiones = cierre.Filas.Where(f => f.AhorroLiquidado + f.InteresAsignado > 0).ToList();
            db.Layouts.Add(new LayoutGenerado
            {
                Tipo = TipoLayout.Spei, CajaId = cs.Caja.Id, CicloId = cerrado.Id, Formato = "CSV",
                NombreArchivo = $"SPEI-{cs.Caja.Prefijo}-{AnioCierre}.csv", Registros = dispersiones.Count,
                ImporteTotal = dispersiones.Sum(f => f.AhorroLiquidado + f.InteresAsignado),
                UsuarioId = _adminId, Fecha = new DateTime(AnioCierre, 12, 31, 19, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            log.WriteLine($"  Ciclo {AnioCierre} de {cs.Caja.Nombre}: interés {cierre.InteresTotal:N2}, {cierre.Filas.Count} participantes");
        }
    }

    private async Task GuardarMovimientosAsync(List<MovTmp> movimientos)
    {
        const int lote = 4_000;
        for (var i = 0; i < movimientos.Count; i += lote)
        {
            var parte = movimientos.Skip(i).Take(lote).ToList();
            foreach (var t in parte)
                if (t.Prestamo is not null)
                {
                    t.Mov.PrestamoId = t.Prestamo.Prestamo.Id;
                    t.Mov.Referencia = t.Prestamo.Prestamo.Folio;     // el folio se asigna antes de guardar
                }
            db.Movimientos.AddRange(parte.Select(t => t.Mov));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private async Task GuardarIncidenciasAsync()
    {
        foreach (var cs in _cajas)
        {
            foreach (var s in cs.Ahorradores)
            {
                foreach (var (idx, tipo) in s.Incid)
                    db.Incidencias.Add(new Incidencia { AhorradorId = s.A.Id, PeriodoId = cs.Periodos[idx].Id, Tipo = tipo });
                if (s.BajaIdx is { } b)
                    db.Incidencias.Add(new Incidencia
                    {
                        AhorradorId = s.A.Id, PeriodoId = cs.Periodos[b].Id, Tipo = TipoIncidencia.Baja, Nota = "Baja del empleado",
                    });
                db.Incidencias.AddRange(s.IncidenciasCambio);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    // ---------------------------------------------------------------- interés devengado (procedimiento real)

    private async Task CalcularInteresDevengadoAsync(TextWriter log)
    {
        var cn = db.Database.GetDbConnection();
        if (cn.State != System.Data.ConnectionState.Open) await cn.OpenAsync();
        var tarea = await db.Tareas.SingleAsync(t => t.Clave == "calculo_interes");
        var inicio = DateTime.UtcNow;

        var cortes = 0;
        foreach (var cs in _cajas)
            for (var idx = 0; idx <= IdxUltimoAplicado; idx++)
            {
                await cn.ExecuteAsync("CALL sp_calcular_interes_devengado(@caja, @corte)",
                    new { caja = cs.Caja.Id, corte = cs.Periodos[idx].FechaFin.ToDateTime(TimeOnly.MinValue) });
                cortes++;
            }
        var renglones = await cn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM interes_devengado");

        var fin = DateTime.UtcNow;
        tarea.UltimaEjecucion = fin;
        db.TareaEjecuciones.Add(new TareaEjecucion
        {
            TareaId = tarea.Id, Inicio = inicio, Fin = fin, Estado = EstadoEjecucion.Exito,
            Resumen = $"Carga inicial: {cortes} cortes calculados, {renglones} renglones de interés devengado.",
        });
        await db.SaveChangesAsync();
        log.WriteLine($"Interés devengado: {cortes} cortes, {renglones:N0} renglones.");
    }

    // ---------------------------------------------------------------- utilidades

    private void Agregar(CajaSim cs, Sim s, TipoMovimiento tipo, decimal monto, DateOnly fecha, Periodo? periodo,
        SimPrestamo? prestamo, string? referencia = null)
    {
        if (monto <= 0) return;
        cs.Movs.Add(new MovTmp(new Movimiento
        {
            CajaId = cs.Caja.Id, AhorradorId = s.A.Id, PeriodoId = periodo?.Id, Fecha = fecha, Tipo = tipo, Monto = monto,
            Referencia = referencia ?? (periodo is null ? null : $"Q{periodo.Numero}-{periodo.Anio}"),
        }, prestamo));
        if (tipo is TipoMovimiento.AhorroOrdinario or TipoMovimiento.AhorroVoluntario) s.Aportes.Add(new CambioAhorro(fecha, monto));
        else if (tipo == TipoMovimiento.LiquidacionCiclo && referencia == "BAJA") s.Liquidaciones.Add(new CambioAhorro(fecha, monto));
    }

    private sealed class CajaSim(Caja caja, int cantidad, Random rnd)
    {
        public Caja Caja { get; } = caja;
        public int Cantidad { get; } = cantidad;
        public Random Rnd { get; } = rnd;
        public List<Periodo> Periodos { get; } = [];
        public List<Sim> Ahorradores { get; } = [];
        public List<MovTmp> Movs { get; } = [];
        public List<SimPrestamo> Prestamos { get; } = [];
        public decimal Interes2023 { get; set; }
        public CierreSim? Cierre { get; set; }
    }

    private sealed class Sim(Ahorrador a)
    {
        public Ahorrador A { get; } = a;
        public bool EsAxel { get; init; }
        public bool Especial { get; set; }
        public bool Tardio { get; set; }
        public int PrimerIdx { get; set; }
        public decimal Ord { get; set; }
        public decimal Ord2024 { get; set; }
        public int? VolDesde { get; set; }
        public int? VolHasta { get; set; }
        public decimal Vol { get; set; }
        public int? BajaIdx { get; set; }
        public DateOnly? FechaBaja { get => A.FechaBaja; set => A.FechaBaja = value; }
        public Dictionary<int, TipoIncidencia> Incid { get; } = [];
        public List<Incidencia> IncidenciasCambio { get; } = [];
        public List<CambioAhorro> Aportes { get; } = [];
        public List<CambioAhorro> Liquidaciones { get; } = [];
        public decimal Saldo { get; set; }
        public SimPrestamo? Prestamo { get; set; }
        public decimal SaldoPrestamo { get; set; }
        public bool AvalUsado { get; set; }

        public (decimal Ord, decimal Vol, bool Activo) Cuota(int idx)
        {
            var ord = idx >= IdxQ1De2024 ? Ord2024 : Ord;
            var activo = VolDesde is { } d && idx >= d && (VolHasta is not { } h || idx < h);
            return (ord, VolDesde is null ? 0m : Vol, activo);
        }
    }

    private sealed record CuotaSim(int PeriodoIdx, PrestamoAmortizacion Fila);
    private sealed class SimPrestamo(Prestamo p)
    {
        public Prestamo Prestamo { get; } = p;
        public List<CuotaSim> Cuotas { get; } = [];
    }
    private sealed record MovTmp(Movimiento Mov, SimPrestamo? Prestamo);
    private sealed class CierreSim(decimal interesTotal)
    {
        public decimal InteresTotal { get; } = interesTotal;
        public List<CicloDistribucion> Filas { get; } = [];
    }
}
