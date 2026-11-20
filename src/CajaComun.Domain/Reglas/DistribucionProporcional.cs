namespace CajaComun.Domain.Reglas;

public static class DistribucionProporcional
{
    /// <summary>
    /// Reparte <paramref name="total"/> en proporción a <paramref name="pesos"/> por el método del mayor
    /// residuo: cada quien recibe la parte entera en centavos y los centavos sobrantes se asignan a los
    /// mayores residuos, de modo que la suma es exactamente el total.
    /// </summary>
    public static decimal[] Repartir(decimal total, IReadOnlyList<decimal> pesos)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (pesos.Any(p => p < 0)) throw new ArgumentException("Los pesos no pueden ser negativos.", nameof(pesos));

        var resultado = new decimal[pesos.Count];
        var sumaPesos = pesos.Sum();
        if (pesos.Count == 0 || sumaPesos == 0) return resultado;

        var centavosTotal = decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var enteros = new long[pesos.Count];
        var residuos = new decimal[pesos.Count];
        long asignados = 0;

        for (var i = 0; i < pesos.Count; i++)
        {
            var exacto = centavosTotal * pesos[i] / sumaPesos;
            enteros[i] = (long)decimal.Floor(exacto);
            residuos[i] = exacto - enteros[i];
            asignados += enteros[i];
        }

        var faltan = (long)centavosTotal - asignados;
        // Empate de residuos: gana el de menor índice, para que el resultado sea determinista.
        foreach (var i in Enumerable.Range(0, pesos.Count)
                     .OrderByDescending(i => residuos[i]).ThenBy(i => i).Take((int)faltan))
            enteros[i]++;

        for (var i = 0; i < pesos.Count; i++) resultado[i] = enteros[i] / 100m;
        return resultado;
    }
}
