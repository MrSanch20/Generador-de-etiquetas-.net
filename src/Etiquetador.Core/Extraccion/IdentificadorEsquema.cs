using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Busca el número de instalación/proyecto del cajetín, que sirve para separar las etiquetas de cada esquema.</summary>
public static class IdentificadorEsquema
{
    // Número del esquema (para separar las etiquetas de cada esquema)
    private static readonly Regex EtiqNumeroRe = Rx.Crear(
        @"N\s*[º°oO]\.?\s*(?:DE\s+INSTALACI[ÓO]N|D'INSTALLATION|DE\s+PROYECTO|PROYECTO|PROJECT)", RegexOptions.IgnoreCase);

    private static readonly Regex ValorNumeroRe = Rx.Crear(@"[A-Z0-9][A-Z0-9_\-./]{3,}", RegexOptions.IgnoreCase);

    /// <summary>Número de instalación/proyecto del cajetín ("Nº DE INSTALACIÓN: CECE00000000001"), o null.</summary>
    public static string? Buscar(IReadOnlyList<Pagina> paginas)
    {
        foreach (var pag in paginas.Take(5))
        {
            foreach (var t in pag.Trozos)
            {
                var m = EtiqNumeroRe.Match(t.Texto);
                if (!m.Success) continue;
                // el número puede ir en el mismo texto ("...: CECE…") o en el texto de su derecha
                string resto = t.Texto[(m.Index + m.Length)..].Trim(' ', ':');
                if (Rx.Completa(ValorNumeroRe, resto.Length > 0 ? resto : "-")) return resto;
                double alto = Math.Max(t.Y1 - t.Y0, 1);
                var derecha = pag.Trozos.Where(o => !ReferenceEquals(o, t) && o.X0 >= t.X1 - 1
                                                    && Math.Abs((o.Y0 + o.Y1) / 2 - (t.Y0 + t.Y1) / 2) < alto * 0.6).OrderBy(o => o.X0);
                foreach (var o in derecha)
                    if (Rx.Completa(ValorNumeroRe, o.Texto.Trim())) return o.Texto.Trim();
            }
        }
        return null;
    }
}
