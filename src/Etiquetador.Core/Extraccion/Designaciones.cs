using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Reconoce designaciones de elementos (KM100, Q20…) y sus tipos, y las ordena de forma natural.</summary>
public static class Designaciones
{
    // Designación de elemento: letras + número (KM100, Q20, H20.1, ATS1.0, S0.1…)
    internal static readonly Regex EquipoRe = Rx.Crear(@"[A-Z]+\d+(?:\.\d+)?");

    internal static readonly Regex NoEquipoRe = Rx.Crear(@"^(L\d|N|PE)$");

    internal static readonly Regex SoloLetrasRe = Rx.Crear(@"[A-Z]{2,}");

    public static readonly (string Pref, string Tipo)[] TiposLista =
    {
        ("ATS", "Arrancador suave"), ("KM", "Contactor"), ("FC", "Final de carrera"),
        ("K", "Relé"), ("Q", "Interruptor / guardamotor"), ("F", "Protección"), ("H", "Piloto"),
        ("S", "Pulsador / selector"), ("M", "Motor"), ("U", "Fuente / equipo electrónico"),
        ("X", "Regleta de bornas"), ("R", "Resistencia"),
    };

    internal static readonly Dictionary<string, string> Tipos = TiposLista.ToDictionary(p => p.Pref, p => p.Tipo);

    internal static readonly (string Pref, string Tipo)[] TiposPorLongitud =
        TiposLista.OrderByDescending(p => p.Pref.Length).ToArray();   // estable: el más largo primero

    public static readonly HashSet<string> DentroArmario = new() { "Contactor", "Relé", "Interruptor / guardamotor", "Protección", "Arrancador suave" };

    internal static readonly HashSet<string> TiposPlaca = new(DentroArmario) { "Fuente / equipo electrónico", "Resistencia" };

    internal static readonly HashSet<string> FueraOPuerta = new() { "Motor", "Piloto", "Pulsador / selector", "Final de carrera" };

    /// <summary>Tipo según el prefijo de letras (el más largo que coincida).</summary>
    public static string TipoEquipo(string designacion)
    {
        var m = Regex.Match(designacion.ToUpperInvariant(), @"\A[A-Z]+");
        string letras = m.Success ? m.Value : "";
        foreach (var (pref, tipo) in TiposPorLongitud)
            if (letras.StartsWith(pref, StringComparison.Ordinal)) return tipo;
        return "Otros";
    }

    /// <summary>Clave de orden natural: (letras en mayúsculas, números, texto).</summary>
    public static (string Letras, List<double> Numeros, string Texto) OrdenNatural(string d)
    {
        var m = Regex.Match(d, @"\A[A-Za-z]+");
        var nums = Regex.Matches(d, @"\d+(?:\.\d+)?").Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
        return (m.Success ? m.Value.ToUpperInvariant() : "", nums, d);
    }

    internal static int CmpOrdenNatural(string a, string b)
    {
        var x = OrdenNatural(a); var y = OrdenNatural(b);
        int c = string.CompareOrdinal(x.Letras, y.Letras);
        if (c != 0) return c;
        int n = Math.Min(x.Numeros.Count, y.Numeros.Count);
        for (int i = 0; i < n; i++)
        {
            c = x.Numeros[i].CompareTo(y.Numeros[i]);
            if (c != 0) return c;
        }
        c = x.Numeros.Count.CompareTo(y.Numeros.Count);
        if (c != 0) return c;
        return string.CompareOrdinal(x.Texto, y.Texto);
    }
}
