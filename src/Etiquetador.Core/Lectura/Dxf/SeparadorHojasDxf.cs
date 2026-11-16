using System.Globalization;

namespace Etiquetador.Core.Lectura.Dxf;

/// <summary>Separa las hojas del espacio modelo cuando hay varias en el mismo dibujo (franjas vacías de texto y líneas).</summary>
internal static class SeparadorHojasDxf
{
    /// <summary>Rectángulos (xmin, xmax, ymin, ymax) de líneas y polilíneas del dibujo.</summary>
    internal static List<(double, double, double, double)> CajasGeometria(IEnumerable<EntidadDxf> entidades)
    {
        var res = new List<(double, double, double, double)>();
        foreach (var e in entidades)
        {
            try
            {
                var pts = new List<(double X, double Y)>();
                switch (e.Tipo)
                {
                    case "LINE":
                        pts.Add((e.Num(10), e.Num(20))); pts.Add((e.Num(11), e.Num(21)));
                        break;
                    case "LWPOLYLINE":
                    {
                        double? x = null;
                        foreach (var (c, v) in e.Datos)
                        {
                            if (c == 10) x = double.Parse(v.Trim(), CultureInfo.InvariantCulture);
                            else if (c == 20 && x != null) { pts.Add((x.Value, double.Parse(v.Trim(), CultureInfo.InvariantCulture))); x = null; }
                        }
                        break;
                    }
                    case "POLYLINE":
                        foreach (var v in e.Hijos) pts.Add((v.Num(10), v.Num(20)));
                        break;
                    default: continue;
                }
                if (pts.Count > 0)
                    res.Add((pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y)));
            }
            catch (Exception) { }
        }
        return res;
    }

    /// <summary>Une intervalos (a, b) que se solapan o tocan; devuelve los grupos resultantes.</summary>
    private static List<double[]> Grupos(IEnumerable<(double, double)> intervalos)
    {
        var grupos = new List<double[]>();
        foreach (var (a, b) in intervalos.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            if (grupos.Count > 0 && a <= grupos[^1][1]) grupos[^1][1] = Math.Max(grupos[^1][1], b);
            else grupos.Add(new[] { a, b });
        }
        return grupos;
    }

    /// <summary>
    /// Si el espacio modelo tiene varias hojas (una al lado de otra o en cuadrícula), las separa por las franjas
    /// vacías: ni textos ni líneas (el marco de cada hoja la mantiene unida).
    /// </summary>
    internal static List<List<TextoDxf>> SepararHojas(List<TextoDxf> textos, List<(double, double, double, double)> cajas)
    {
        static (double, double, double, double) CajaTexto(TextoDxf t) =>
            Math.Abs(((t.Rot % 360) + 360) % 360 - 90) < 5 ? (t.X - t.Alto, t.X, t.Y, t.Y + t.Ancho) : (t.X, t.X + t.Ancho, t.Y, t.Y + t.Alto);

        var elementos = textos.Select(t => (K: 't', C: CajaTexto(t), T: (TextoDxf?)t)).ToList();
        elementos.AddRange(cajas.Select(c => (K: 'g', C: c, T: (TextoDxf?)null)));
        const double margen = 1e-6;
        var hojas = new List<(double X, double Y, List<TextoDxf> Textos)>();
        foreach (var gx in Grupos(elementos.Select(e => (e.C.Item1 - margen, e.C.Item2 + margen))))
        {
            var col = elementos.Where(e => gx[0] <= (e.C.Item1 + e.C.Item2) / 2 && (e.C.Item1 + e.C.Item2) / 2 <= gx[1]).ToList();
            foreach (var gy in Grupos(col.Select(e => (e.C.Item3 - margen, e.C.Item4 + margen))).OrderBy(g => -g[1]))
            {
                var hoja = col.Where(e => e.K == 't' && gy[0] <= (e.C.Item3 + e.C.Item4) / 2 && (e.C.Item3 + e.C.Item4) / 2 <= gy[1])
                              .Select(e => e.T!).ToList();
                if (hoja.Count >= 3) hojas.Add((gx[0], -gy[1], hoja));
            }
        }
        // de arriba abajo y de izquierda a derecha
        var ordenadas = hojas.OrderBy(h => (int)Math.Round(h.Y, MidpointRounding.ToEven)).ThenBy(h => h.X).Select(h => h.Textos).ToList();
        return ordenadas.Count > 0 ? ordenadas : new List<List<TextoDxf>> { textos };
    }
}
