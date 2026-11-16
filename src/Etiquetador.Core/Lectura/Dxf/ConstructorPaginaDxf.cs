namespace Etiquetador.Core.Lectura.Dxf;

/// <summary>Convierte los textos de una hoja en una <see cref="Pagina"/> (coordenadas normalizadas al alto de referencia).</summary>
internal static class ConstructorPaginaDxf
{
    internal static Pagina? Crear(int numero, List<TextoDxf> textos, string origen = "dxf")
    {
        if (textos.Count == 0) return null;
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var t in textos)
        {
            xs.Add(t.X); xs.Add(t.X + t.Ancho);
            ys.Add(t.Y); ys.Add(t.Y + t.Alto);
        }
        double xmin = xs.Min(), xmax = xs.Max(), ymin = ys.Min(), ymax = ys.Max();
        double altoPag = Math.Max(ymax - ymin, 1e-6) * 1.02;
        double esc = LectorArchivos.AltoRef / altoPag;
        var pag = new Pagina(numero, (xmax - xmin) * esc, LectorArchivos.AltoRef, origen);
        foreach (var t in textos)
        {
            double r = ((t.Rot % 360) + 360) % 360;
            Direccion d; double x0, x1, yb0, yb1;
            if (Math.Abs(r - 90) < 5)
            {
                d = Direccion.Vertical;
                // texto girado 90°: crece hacia arriba desde (x, y); el cuerpo queda a la izquierda de x
                x0 = t.X - t.Alto; x1 = t.X;
                yb0 = t.Y; yb1 = t.Y + t.Ancho;
            }
            else
            {
                d = (r < 5 || r > 355) ? Direccion.Horizontal
                    : Direccion.Redondeada(Math.Cos(r * Math.PI / 180), -Math.Sin(r * Math.PI / 180));
                x0 = t.X; x1 = t.X + t.Ancho;
                yb0 = t.Y; yb1 = t.Y + t.Alto;
            }
            double X0 = (x0 - xmin) * esc, X1 = (x1 - xmin) * esc;
            double Y0 = (ymax - yb1) * esc, Y1 = (ymax - yb0) * esc;
            var tp = new TextoPos(t.Texto, X0, Y0, X1, Y1, d, t.Alto * esc, t.Estilo);
            pag.Trozos.Add(tp);
            pag.Lineas.Add(tp);
        }
        return pag;
    }
}
