using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.AhorroVoluntario;

public sealed record ConsultaVoluntario : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public string? Q { get; init; }
    /// <summary>Verdadero solo con voluntario activo; falso, solo sin él.</summary>
    public bool? Activo { get; init; }
}

public sealed record VoluntarioDto(
    int AhorradorId, int CajaId, string NumEmpleado, string Nombre, EstadoAhorrador Estado, bool Activo,
    decimal CuotaVoluntaria, DateOnly? VigenteDesde, decimal AcumuladoVoluntario, decimal AhorroTotal);

public sealed record ResumenVoluntarioDto(int AhorradoresActivos, decimal CuotaPorPeriodo, decimal Acumulado);

/// <summary>Sin VigenteDesde la cuota rige desde el primer periodo abierto. Solo se capturan aportaciones; no hay retiros.</summary>
public sealed record ActualizarVoluntarioRequest(bool Activo, decimal CuotaVoluntaria, DateOnly? VigenteDesde);

public interface IAhorroVoluntarioService
{
    Task<Pagina<VoluntarioDto>> ListarAsync(ConsultaVoluntario consulta, CancellationToken ct = default);
    Task<ResumenVoluntarioDto> ResumenAsync(int? cajaId, CancellationToken ct = default);
    Task<VoluntarioDto> ActualizarAsync(int ahorradorId, ActualizarVoluntarioRequest request, CancellationToken ct = default);
}

public sealed class ActualizarVoluntarioRequestValidator : AbstractValidator<ActualizarVoluntarioRequest>
{
    public ActualizarVoluntarioRequestValidator()
    {
        RuleFor(x => x.CuotaVoluntaria).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO")
            .PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.CuotaVoluntaria).GreaterThan(0m).WithErrorCode("MONTO_INVALIDO").When(x => x.Activo);
        RuleFor(x => x.CuotaVoluntaria).Equal(0m).WithErrorCode("VOLUNTARIO_INACTIVO").When(x => !x.Activo);
    }
}
