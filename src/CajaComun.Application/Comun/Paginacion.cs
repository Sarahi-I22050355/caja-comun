namespace CajaComun.Application.Comun;

public sealed record Pagina<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Parámetros de paginación del lado del servidor, con tope para que nadie pida la tabla completa.</summary>
public record ConsultaPaginada
{
    public const int PageSizeMaximo = 100;
    public const int PageSizePorDefecto = 25;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageSizePorDefecto;

    public int PaginaSegura => Math.Max(1, Page);
    public int TamanoSeguro => Math.Clamp(PageSize, 1, PageSizeMaximo);
    public int Saltar => (PaginaSegura - 1) * TamanoSeguro;
}
