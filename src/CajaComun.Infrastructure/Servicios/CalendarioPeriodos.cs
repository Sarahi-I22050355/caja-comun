using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

/// <summary>Consultas de periodos que comparten la nómina y los préstamos.</summary>
internal sealed class CalendarioPeriodos(CajaComunDbContext db)
{
    public Task<Periodo?> PrimerAbiertoAsync(int cajaId, CancellationToken ct) =>
        db.Periodos.AsNoTracking()
            .Where(p => p.CajaId == cajaId && p.Estado == EstadoPeriodo.Abierto)
            .OrderBy(p => p.FechaInicio)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Los <paramref name="cantidad"/> periodos que siguen a <paramref name="despuesDe"/>. Si la caja no los tiene
    /// todos (un préstamo que cruza el fin de su ciclo), los del año siguiente se generan: con
    /// <paramref name="crear"/> se guardan; sin él solo se proyectan, con Id 0, para simular.
    /// </summary>
    public async Task<IReadOnlyList<Periodo>> SiguientesAsync(
        Caja caja, Periodo despuesDe, int cantidad, bool crear, CancellationToken ct)
    {
        var lista = await db.Periodos.AsNoTracking()
            .Where(p => p.CajaId == caja.Id && p.FechaInicio > despuesDe.FechaInicio)
            .OrderBy(p => p.FechaInicio)
            .Take(cantidad)
            .ToListAsync(ct);

        var nuevos = new List<Periodo>();
        while (lista.Count + nuevos.Count < cantidad)
        {
            var ultimo = nuevos.Count > 0 ? nuevos[^1] : lista.Count > 0 ? lista[^1] : despuesDe;
            var anio = ultimo.Anio + 1;
            foreach (var r in PeriodosCiclo.Generar(caja.FrecuenciaNomina, anio))
            {
                if (lista.Count + nuevos.Count >= cantidad) break;
                nuevos.Add(new Periodo { CajaId = caja.Id, Anio = anio, Numero = r.Numero, FechaInicio = r.Inicio, FechaFin = r.Fin });
            }
        }

        if (nuevos.Count > 0 && crear)
        {
            db.Periodos.AddRange(nuevos);
            await db.SaveChangesAsync(ct);
        }
        return [.. lista, .. nuevos];
    }
}
