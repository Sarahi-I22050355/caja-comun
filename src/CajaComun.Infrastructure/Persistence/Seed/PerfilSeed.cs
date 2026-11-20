namespace CajaComun.Infrastructure.Persistence.Seed;

/// <summary>Tamaño de la población de prueba. Con la misma semilla el resultado es siempre idéntico.</summary>
public sealed record PerfilSeed(
    string Nombre, int Gimsa, int Aceros, int Transportes, int PrimerNumeroGimsa, double FactorPrestamos)
{
    // En demo la numeración de GIMSA arranca en 100 para que GIM-00123 exista con solo 60 personas.
    public static readonly PerfilSeed Demo = new("demo", 60, 25, 15, 100, 2.0);
    public static readonly PerfilSeed Full = new("full", 1248, 640, 312, 1, 1.0);

    public static PerfilSeed? Buscar(string nombre) => nombre.ToLowerInvariant() switch
    {
        "demo" => Demo,
        "full" => Full,
        _ => null
    };
}
