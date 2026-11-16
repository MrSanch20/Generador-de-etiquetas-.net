using System.Text;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Lectura.Dxf;

/// <summary>Extrae TEXT, MTEXT y ATTRIB (también dentro de bloques, con su transformación) y limpia los códigos de formato.</summary>
internal static class ExtractorTextosDxf
{
    private static string LimpiarTexto(string s)
    {
        // %%d = °, %%p = ±, %%c = Ø, %%% = %
        s = s.Replace("%%d", "°", StringComparison.OrdinalIgnoreCase)
             .Replace("%%p", "±", StringComparison.OrdinalIgnoreCase)
             .Replace("%%c", "Ø", StringComparison.OrdinalIgnoreCase)
             .Replace("%%%", "%");
        s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
        return s;
    }

    /// <summary>Texto de un MTEXT sin los códigos de formato (\P, \f…;, {…}).</summary>
    private static string TextoMtext(EntidadDxf e)
    {
        var sb = new StringBuilder();
        foreach (var c in e.Todos(3)) sb.Append(c);
        sb.Append(e.Str(1) ?? "");
        string s = sb.ToString();
        s = s.Replace("\\\\", "\u0001").Replace("\\{", "\u0002").Replace("\\}", "\u0003");
        s = Regex.Replace(s, @"\\P", "\n");
        s = Regex.Replace(s, @"\\S([^;^]*)[\^/#]([^;]*);", "$1/$2");
        s = Regex.Replace(s, @"\\[fFcCHhTtQqWwAa][^;]*;", "");
        s = Regex.Replace(s, @"\\[LlOoKk]", "");
        s = s.Replace("\\~", " ").Replace("{", "").Replace("}", "");
        s = s.Replace("\u0001", "\\").Replace("\u0002", "{").Replace("\u0003", "}");
        return LimpiarTexto(s);
    }

    private static (double X, double Y) Dir(double grados, double largo)
    {
        double a = grados * Math.PI / 180;
        return (Math.Cos(a) * largo, Math.Sin(a) * largo);
    }

    internal static IEnumerable<TextoDxf> Textos(IEnumerable<EntidadDxf> entidades, DocumentoDxf dxf, Transf t, int profundidad = 0)
    {
        foreach (var e in entidades)
        {
            List<TextoDxf> salida = new();
            try
            {
                switch (e.Tipo)
                {
                    case "INSERT":
                        foreach (var a in e.Hijos)
                            if ((a.Ent32(70) & 1) == 0)
                                salida.AddRange(Textos(new[] { a }, dxf, Transf.Identidad, profundidad + 1));
                        if (profundidad < 8 && e.Str(2) is string nombre && dxf.Bloques.TryGetValue(nombre, out var b))
                        {
                            double sx = e.Num(41, 1), sy = e.Num(42, 1);
                            var (ix, iy) = t.Punto(e.Num(10), e.Num(20));
                            var nt = new Transf(ix, iy, sx * t.Sx, sy * t.Sy, e.Num(50) + t.RotGrados, b.BaseX, b.BaseY);
                            salida.AddRange(Textos(b.Entidades, dxf, nt, profundidad + 1));
                        }
                        break;
                    case "TEXT":
                    case "ATTRIB":
                    {
                        string txt = LimpiarTexto(e.Str(1) ?? "").Trim();
                        if (txt.Length == 0) break;
                        double alto = e.Num(40) * Math.Abs(t.Sy);
                        double rot = e.Num(50) + t.RotGrados;
                        double px = e.Num(10), py = e.Num(20);
                        int halign = e.Ent32(72), valign = e.Ent32(73);
                        if ((halign != 0 || valign != 0) && e.Tiene(11)) { px = e.Num(11); py = e.Num(21); }
                        var (wx, wy) = t.Punto(px, py);
                        double ancho = txt.Length * alto * 0.75;
                        if (halign == 1 || halign == 4) { var d = Dir(rot, ancho / 2); wx -= d.X; wy -= d.Y; }
                        else if (halign == 2) { var d = Dir(rot, ancho); wx -= d.X; wy -= d.Y; }
                        salida.Add(new TextoDxf(txt, wx, wy, rot, alto,
                            (Math.Round(alto, 2, MidpointRounding.ToEven), e.Str(8) ?? "0", e.Ent32(62, 256)), ancho));
                        break;
                    }
                    case "MTEXT":
                    {
                        string txt = TextoMtext(e).Trim();
                        if (txt.Length == 0) break;
                        double alto = e.Num(40) * Math.Abs(t.Sy);
                        double rot = e.Tiene(11) ? Math.Atan2(e.Num(21), e.Num(11)) * 180 / Math.PI : e.Num(50);
                        rot += t.RotGrados;
                        var (px, py) = t.Punto(e.Num(10), e.Num(20));
                        var lineas = txt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                        double ancho = lineas.Max(l => l.Length) * alto * 0.75;
                        int att = e.Tiene(71) ? e.Ent32(71) : 1;
                        int fila = (att - 1) / 3, col = (att - 1) % 3;
                        int nLineas = lineas.Count;
                        var d1 = Dir(rot, ancho * col / 2);
                        px -= d1.X; py -= d1.Y;
                        var d2 = Dir(rot + 90, alto * nLineas * (1 - fila / 2.0) - alto);
                        px += d2.X; py += d2.Y;
                        for (int i = 0; i < lineas.Count; i++)
                        {
                            if (lineas[i].Trim().Length == 0) continue;
                            var d3 = Dir(rot + 90, i * alto * 1.6);
                            salida.Add(new TextoDxf(lineas[i].Trim(), px - d3.X, py - d3.Y, rot, alto,
                                (Math.Round(alto, 2, MidpointRounding.ToEven), e.Str(8) ?? "0", e.Ent32(62, 256)),
                                lineas[i].Length * alto * 0.75));
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                continue;      // una entidad rara no debe parar la lectura
            }
            foreach (var s in salida) yield return s;
        }
    }
}
