using System.Text.RegularExpressions;

namespace Etiquetador.Core.Utilidades;

/// <summary>Atajos de expresiones regulares con la semántica de re.fullmatch / re.match / re.search de Python.</summary>
public static class Rx
{
    public static Regex Crear(string patron, RegexOptions opciones = RegexOptions.None) =>
        new(patron, opciones | RegexOptions.CultureInvariant);

    /// <summary>re.fullmatch(patrón, texto): la coincidencia ocupa todo el texto.</summary>
    public static bool Completa(Regex re, string texto)
    {
        // Se vuelve a evaluar con anclas: new Regex(@"\A(?:patrón)\z")
        return Anclada(re).IsMatch(texto);
    }

    private static readonly Dictionary<Regex, Regex> _cacheAnclada = new();

    private static Regex Anclada(Regex re)
    {
        lock (_cacheAnclada)
        {
            if (!_cacheAnclada.TryGetValue(re, out var a))
            {
                a = new Regex(@"\A(?:" + re.ToString() + @")\z", re.Options);
                _cacheAnclada[re] = a;
            }
            return a;
        }
    }

    /// <summary>Resultado de re.fullmatch, con sus grupos (o null).</summary>
    public static Match? CompletaMatch(Regex re, string texto)
    {
        var m = Anclada(re).Match(texto);
        return m.Success ? m : null;
    }

    /// <summary>re.match(patrón, texto): coincide al principio del texto.</summary>
    public static Match? Principio(Regex re, string texto)
    {
        var m = new Regex(@"\A(?:" + re.ToString() + ")", re.Options).Match(texto);
        return m.Success ? m : null;
    }
}
