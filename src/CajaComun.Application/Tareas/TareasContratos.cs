using CajaComun.Application.Comun;
using CajaComun.Application.Trabajos;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Tareas;

public sealed record TareaDto(
    int Id, string Clave, string Nombre, FrecuenciaTarea Frecuencia, TimeOnly Hora, bool Activa, DateTime? UltimaEjecucion,
    DateTime? ProximaEjecucion, EstadoEjecucion? UltimoEstado, string? UltimoResumen);

/// <summary>La hora es la del reloj de la Ciudad de México (America/Mexico_City).</summary>
public sealed record ActualizarTareaRequest(FrecuenciaTarea Frecuencia, TimeOnly Hora, bool Activa);

public sealed record EjecucionDto(long Id, DateTime Inicio, DateTime? Fin, EstadoEjecucion Estado, string? Resumen);

public interface ITareaService
{
    Task<IReadOnlyList<TareaDto>> ListarAsync(CancellationToken ct = default);
    Task<TareaDto> ObtenerAsync(int id, CancellationToken ct = default);
    /// <summary>Guarda la frecuencia, la hora y el interruptor, y reprograma la tarea de inmediato (sin reiniciar).</summary>
    Task<TareaDto> ActualizarAsync(int id, ActualizarTareaRequest request, CancellationToken ct = default);
    Task<TrabajoDto> EjecutarAhoraAsync(int id, CancellationToken ct = default);
    Task<Pagina<EjecucionDto>> HistorialAsync(int id, ConsultaPaginada consulta, CancellationToken ct = default);
}

/// <summary>Ejecuta una tarea y registra la ejecución (inicio, fin, estado y resumen).</summary>
public interface IEjecutorTareas
{
    Task<EjecucionDto> EjecutarAsync(int tareaId, CancellationToken ct = default);
}

/// <summary>Mantiene la programación en caliente: se avisa cada vez que cambia la configuración de una tarea.</summary>
public interface IProgramadorTareas
{
    Task ReprogramarAsync(int tareaId, CancellationToken ct = default);
    Task<DateTime?> ProximaEjecucionUtcAsync(int tareaId, CancellationToken ct = default);
}

public sealed class ActualizarTareaRequestValidator : AbstractValidator<ActualizarTareaRequest>
{
    public ActualizarTareaRequestValidator() =>
        RuleFor(x => x.Frecuencia).IsInEnum().WithErrorCode("VALOR_INVALIDO");
}
