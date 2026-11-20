namespace CajaComun.Application.Consultas;

/// <summary>Una de las cinco consultas de la Parte 2.</summary>
public sealed record ConsultaDefinicion(int Numero, bool UsaAnio, bool UsaFechaCorte);

public sealed record ColumnaConsulta(string Nombre, string Tipo);

/// <summary>Resultado de ejecutar una consulta federada: una página de filas y el total real de renglones.</summary>
public sealed record ResultadoConsulta(
    int Consulta,
    int? Anio,
    string Sql,
    IReadOnlyList<ColumnaConsulta> Columnas,
    IReadOnlyList<IReadOnlyList<object?>> Filas,
    int Total,
    int Page,
    int PageSize,
    long DuracionMs);

/// <summary>Comparación entre el resultado federado y el de la consulta directa sobre MariaDB.</summary>
public sealed record EquivalenciaDto(
    int Consulta,
    int? Anio,
    int FilasFederadas,
    int FilasDirectas,
    bool Coincide,
    int Diferencias,
    string? Ejemplo,
    string SqlDirecto,
    long DuracionFederadaMs,
    long DuracionDirectaMs);

public sealed record MotorDto(string Motor, string Catalogo, string Cajas, int Ahorradores, int Movimientos);

/// <param name="Motivo">Código estable de por qué no está disponible (FEDERACION_NO_DISPONIBLE, CONSULTA_FEDERADA_FALLO).</param>
public sealed record EstadoFederacionDto(bool Disponible, string? Motivo, IReadOnlyList<MotorDto> Motores, IReadOnlyList<ConsultaDefinicion> Consultas);

public interface IConsultasDistribuidasService
{
    /// <summary>Qué consultas existen y si la federación responde, con los renglones que hay en cada motor.</summary>
    Task<EstadoFederacionDto> EstadoAsync(CancellationToken ct = default);

    Task<ResultadoConsulta> EjecutarAsync(int consulta, int? anio, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Todo el resultado (sin paginar) como CSV, con el SQL federado que lo produjo.</summary>
    Task<(string Nombre, byte[] Contenido)> ExportarCsvAsync(int consulta, int? anio, CancellationToken ct = default);

    Task<EquivalenciaDto> VerificarEquivalenciaAsync(int consulta, int? anio, CancellationToken ct = default);
}

/// <summary>Lo que hace falta para cargar los motores de la federación y comprobar que lo cargado es igual al origen.</summary>
public interface ICargaFederacion
{
    Task<IReadOnlyList<string>> CargarAsync(CancellationToken ct = default);
}
