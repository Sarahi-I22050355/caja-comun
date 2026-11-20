using System.Globalization;
using System.Text;
using CajaComun.Domain.Reglas;

namespace CajaComun.Infrastructure.Persistence.Seed;

/// <summary>Nombres, RFC y CLABE sintéticos para la población de prueba.</summary>
internal static class DatosPersonas
{
    private static readonly string[] NombresH =
    [
        "José", "Juan", "Luis", "Carlos", "Miguel Ángel", "Jorge", "Francisco", "Pedro", "Alejandro", "Ricardo",
        "Fernando", "Eduardo", "Daniel", "Javier", "Raúl", "Sergio", "Héctor", "Óscar", "Gerardo", "Mario",
        "Arturo", "Rodrigo", "Iván", "Adrián", "Víctor Manuel", "Emilio", "Gustavo", "Ramón", "Cristian", "Hugo"
    ];

    private static readonly string[] NombresM =
    [
        "María", "Guadalupe", "Ana Lucía", "Patricia", "Laura", "Claudia", "Verónica", "Alejandra", "Sandra", "Elena",
        "Karla", "Daniela", "Fernanda", "Lorena", "Gabriela", "Mónica", "Rosa Elena", "Teresa", "Diana", "Paola",
        "Brenda", "Norma", "Leticia", "Yesenia", "Martha", "Silvia", "Karen", "Liliana", "Itzel", "Rocío"
    ];

    private static readonly string[] Apellidos =
    [
        "Hernández", "García", "Martínez", "López", "González", "Rodríguez", "Pérez", "Sánchez", "Ramírez", "Flores",
        "Gómez", "Díaz", "Reyes", "Cruz", "Morales", "Ortiz", "Gutiérrez", "Chávez", "Ramos", "Vargas",
        "Castillo", "Jiménez", "Moreno", "Romero", "Herrera", "Medina", "Aguilar", "Garza", "Rangel", "Salazar",
        "Treviño", "Cavazos", "Elizondo", "Villarreal", "Saldaña", "Montemayor", "Lozano", "Benavides", "Quintero",
        "Zapata", "Cantú", "Guerra", "Leal", "Alanís", "Ibarra", "Ponce", "Valdés", "Mendoza", "Rivera", "Navarro",
        "Ochoa", "Rojas", "Solís", "Cervantes", "Barrera", "Tamez", "Escobedo", "Lara", "Peña", "Domínguez"
    ];

    private static readonly string[] Departamentos =
    [
        "Producción", "Mantenimiento", "Calidad", "Almacén", "Compras", "Recursos Humanos", "Contabilidad",
        "Seguridad Industrial", "Logística y Cadena de Suministro", "Sistemas", "Ventas", "Operaciones"
    ];

    // Claves de banco de la CLABE (Banxico): Banamex, BBVA, Santander, HSBC, Banorte, Scotiabank.
    private static readonly string[] Bancos = ["002", "012", "014", "021", "072", "044"];
    private const string Homoclave = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";

    public static (string Nombre, string Apellidos) NombreAleatorio(Random r)
    {
        var nombre = r.Next(2) == 0 ? NombresH[r.Next(NombresH.Length)] : NombresM[r.Next(NombresM.Length)];
        var a1 = Apellidos[r.Next(Apellidos.Length)];
        var a2 = Apellidos[r.Next(Apellidos.Length)];
        return (nombre, a1 == a2 ? a1 : $"{a1} {a2}");
    }

    public static string Departamento(Random r) => Departamentos[r.Next(Departamentos.Length)];

    public static string Clabe(Random r)
    {
        var banco = Bancos[r.Next(Bancos.Length)];
        var plaza = r.Next(1, 1000);
        var cuenta = r.NextInt64(0, 100_000_000_000);
        return CajaComun.Domain.Reglas.Clabe.Completar($"{banco}{plaza:D3}{cuenta:D11}");
    }

    public static string Rfc(string nombre, string apellidos, DateOnly nacimiento, Random r)
    {
        var partes = apellidos.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Limpiar).ToArray();
        var paterno = partes[0];
        var materno = partes.Length > 1 ? partes[1] : "X";
        var inicialNombre = Limpiar(nombre)[0];
        var vocal = paterno.Skip(1).FirstOrDefault(c => "AEIOU".Contains(c), 'X');
        var homoclave = new string(Enumerable.Range(0, 3).Select(_ => Homoclave[r.Next(Homoclave.Length)]).ToArray());
        return $"{paterno[0]}{vocal}{materno[0]}{inicialNombre}{nacimiento:yyMMdd}{homoclave}";
    }

    private static string Limpiar(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c))
                sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }
}
