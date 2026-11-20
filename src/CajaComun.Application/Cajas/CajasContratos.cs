using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using FluentValidation;

namespace CajaComun.Application.Cajas;

public sealed record CajaDto(
    int Id, int EmpresaId, string Empresa, string Nombre, string Prefijo, decimal TasaInteresMensual,
    FrecuenciaNomina FrecuenciaNomina, DateOnly InicioCiclo, DateOnly FinCiclo, bool Activa,
    int AhorradoresActivos, decimal AhorroTotal);

public sealed record EmpresaDto(int Id, string Nombre, bool Activa);

/// <summary>Se indica una empresa existente (EmpresaId) o el nombre de una nueva (EmpresaNueva).</summary>
public sealed record CrearCajaRequest(
    int? EmpresaId, string? EmpresaNueva, string Nombre, string Prefijo, decimal TasaInteresMensual,
    FrecuenciaNomina FrecuenciaNomina, int AnioCiclo);

/// <summary>La frecuencia no se cambia: los periodos de la caja ya dependen de ella.</summary>
public sealed record ActualizarCajaRequest(string Nombre, decimal TasaInteresMensual, bool Activa);

public sealed record PeriodoDto(int Id, int Anio, int Numero, DateOnly FechaInicio, DateOnly FechaFin, EstadoPeriodo Estado);

public interface ICajaService
{
    Task<IReadOnlyList<CajaDto>> ListarAsync(CancellationToken ct = default);
    Task<CajaDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<CajaDto> CrearAsync(CrearCajaRequest request, CancellationToken ct = default);
    Task<CajaDto> ActualizarAsync(int id, ActualizarCajaRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<EmpresaDto>> ListarEmpresasAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PeriodoDto>> ListarPeriodosAsync(int cajaId, int? anio, CancellationToken ct = default);
}

public sealed class CrearCajaRequestValidator : AbstractValidator<CrearCajaRequest>
{
    public CrearCajaRequestValidator()
    {
        RuleFor(x => x).Must(x => x.EmpresaId is not null ^ !string.IsNullOrWhiteSpace(x.EmpresaNueva))
            .WithName("empresa").WithErrorCode("EMPRESA_REQUERIDA")
            .WithMessage("Indica una empresa existente o el nombre de una nueva, pero no ambas.");
        RuleFor(x => x.EmpresaNueva).MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Nombre).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Prefijo).Matches("^[A-Z]{2,5}$").WithErrorCode("PREFIJO_INVALIDO");
        RuleFor(x => x.TasaInteresMensual).Must(ReglasCaja.TasaValida).WithErrorCode("TASA_FUERA_DE_RANGO");
        RuleFor(x => x.FrecuenciaNomina).IsInEnum().WithErrorCode("VALOR_INVALIDO");
        RuleFor(x => x.AnioCiclo).InclusiveBetween(2000, 2100).WithErrorCode("ANIO_INVALIDO");
    }
}

public sealed class ActualizarCajaRequestValidator : AbstractValidator<ActualizarCajaRequest>
{
    public ActualizarCajaRequestValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.TasaInteresMensual).Must(ReglasCaja.TasaValida).WithErrorCode("TASA_FUERA_DE_RANGO");
    }
}
