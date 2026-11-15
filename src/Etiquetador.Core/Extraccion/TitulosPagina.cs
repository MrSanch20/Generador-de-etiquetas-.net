using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Lee el título de las páginas de esquema ("Plano de bornes =…-XALI").</summary>
public static class TitulosPagina
{
    // Título de las páginas de bornas, en varios idiomas
    internal static readonly Regex TituloRe = Rx.Crear(@"(?:\bbornes|\bterminals?(?: \w+)?|klemmen\w*)\s*=\s*\S+?-(\S+?)\s*$", RegexOptions.IgnoreCase);

    /// <summary>Nombre de la regleta que dice el título de la página (XALI…), o null.</summary>
    internal static string? RegletaDe(Pagina pagina)
    {
        foreach (var t in pagina.Lineas)
        {
            if (t.Dir == Direccion.Horizontal)
            {
                var m = TituloRe.Match(t.Texto);
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }
}
