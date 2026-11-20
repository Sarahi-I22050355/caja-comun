namespace CajaComun.Domain.Reglas;

public static class Clabe
{
    // Ponderadores del dígito verificador, en ciclo sobre las primeras 17 posiciones.
    private static readonly int[] Pesos = [3, 7, 1];

    public static int DigitoVerificador(string primeros17)
    {
        if (primeros17 is not { Length: 17 } || !primeros17.All(char.IsAsciiDigit))
            throw new ArgumentException("Se requieren 17 dígitos.", nameof(primeros17));

        var suma = 0;
        for (var i = 0; i < 17; i++)
            suma += (primeros17[i] - '0') * Pesos[i % 3] % 10;
        return (10 - suma % 10) % 10;
    }

    public static string Completar(string primeros17) => primeros17 + DigitoVerificador(primeros17);

    public static bool EsValida(string? clabe) =>
        clabe is { Length: 18 }
        && clabe.All(char.IsAsciiDigit)
        && DigitoVerificador(clabe[..17]) == clabe[17] - '0';
}
