namespace CajaComun.Domain.Reglas;

public static class Quincenas
{
    public const int PorAnio = 24;

    /// <summary>Q1 = 1-15 de enero, Q2 = 16-31 de enero, ... Q24 = 16-31 de diciembre.</summary>
    public static (DateOnly Inicio, DateOnly Fin) Rango(int anio, int numero)
    {
        if (numero is < 1 or > PorAnio) throw new ArgumentOutOfRangeException(nameof(numero));
        var mes = (numero + 1) / 2;
        return numero % 2 == 1
            ? (new DateOnly(anio, mes, 1), new DateOnly(anio, mes, 15))
            : (new DateOnly(anio, mes, 16), new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes)));
    }
}
