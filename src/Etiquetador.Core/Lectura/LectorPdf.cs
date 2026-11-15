using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;

namespace Etiquetador.Core.Lectura;

/// <summary>
/// Lectura de PDF con PdfPig (Apache-2.0). Hace lo mismo que la versión Python con pdfplumber:
/// texto agrupado en líneas y trozos, y el dibujo vectorial (rectas, flechas, circulitos) en las
/// páginas que llevan marcas de cable.
/// </summary>
public static class LectorPdf
{
    /// <summary>Separación (en tamaños de letra) a partir de la cual se añade un espacio.</summary>
    public const double Espacio = 0.12;
    /// <summary>Separación (en tamaños de letra) a partir de la cual un texto es otro distinto.</summary>
    public const double HuecoMax = 0.3;
    /// <summary>Una página con más trazados que esto (planos de implantación) no se lee en detalle.</summary>
    public const int MaxDibujos = 60000;
    public const bool RectangulosComoTrazos = false;

    public static List<Pagina> Leer(string ruta, Action<string>? progreso = null)
    {
        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(ruta);
        }
        catch (Exception e)
        {
            var detalle = $"{e.GetType().Name} {e.Message} {e.InnerException}".ToLowerInvariant();
            if (detalle.Contains("password") || detalle.Contains("encrypt"))
                throw new ErrorLectura("El PDF está protegido con contraseña.");
            throw new ErrorLectura($"No se puede abrir el PDF: {e.Message}");
        }

        var paginas = new List<Pagina>();
        using (pdf)
        {
            int total;
            try { total = pdf.NumberOfPages; }
            catch (Exception e) { throw new ErrorLectura($"No se puede abrir el PDF: {e.Message}"); }

            for (int num = 1; num <= total; num++)
            {
                progreso?.Invoke($"Leyendo página {num} de {total}…");
                Page page;
                try { page = pdf.GetPage(num); }
                catch (Exception e)
                {
                    var detalle = $"{e.GetType().Name} {e.Message}".ToLowerInvariant();
                    if (detalle.Contains("password") || detalle.Contains("encrypt"))
                        throw new ErrorLectura("El PDF está protegido con contraseña.");
                    throw new ErrorLectura($"No se puede leer la página {num} del PDF: {e.Message}");
                }

                double alto = page.Height;
                double esc = LectorArchivos.AltoRef / alto;
                var pag = new Pagina(num, page.Width * esc, LectorArchivos.AltoRef);

                List<CaracterPdf> chars;
                try { chars = Caracteres(page); }
                catch (Exception e) { throw new ErrorLectura($"No se puede leer la página {num} del PDF: {e.Message}"); }

                foreach (var (d, spansTodos) in AgruparTexto(chars))
                {
                    var spans = spansTodos.Where(s => s.Texto.Trim().Length > 0).ToList();
                    if (spans.Count == 0) continue;
                    foreach (var s in spans)
                        pag.Trozos.Add(new TextoPos(s.Texto.Trim(), s.X0 * esc, s.Y0 * esc, s.X1 * esc, s.Y1 * esc, d, s.Tam * esc, s.Color));
                    double x0 = spans.Min(s => s.X0) * esc;
                    double y0 = spans.Min(s => s.Y0) * esc;
                    double x1 = spans.Max(s => s.X1) * esc;
                    double y1 = spans.Max(s => s.Y1) * esc;
                    string texto = string.Concat(spans.Select(s => s.Texto)).Trim();
                    pag.Lineas.Add(new TextoPos(texto, x0, y0, x1, y1, d, spans[0].Tam * esc, spans[0].Color));
                }

                if (pag.Trozos.Count >= 3 && LlevaCables(pag) && page.Paths.Count <= MaxDibujos)
                {
                    try { LeerDibujo(page, pag, esc, alto); }
                    catch (Exception)
                    {
                        // sin dibujo no se pueden seguir los cables, pero el resto sigue valiendo
                        pag.Trazos.Clear(); pag.Flechas.Clear(); pag.Circulos.Clear();
                    }
                }

                if (pag.Trozos.Count < 3)
                {
                    // Página sin texto: probablemente escaneada -> OCR
                    progreso?.Invoke($"Página {num}: sin texto, aplicando OCR…");
                    if (LectorOcr.PuedeLeerPaginaPdf)
                        pag = LectorOcr.PaginaPdf(ruta, num, progreso);
                    else
                        pag.SinOcr = true;     // sin OCR: se salta esta página y se sigue con las demás
                }
                paginas.Add(pag);
            }
        }
        return paginas;
    }

    /// <summary>¿Tiene la página textos de color de cable (NE, MA, AZ…)? Barato de comprobar.</summary>
    private static bool LlevaCables(Pagina pag) =>
        pag.Trozos.Any(t => t.Dir == Direccion.Horizontal && Rx.Completa(ExtractorCables.ColorCableRe, t.Texto));

    // ----------------------------------------------------------------------
    // Texto
    // ----------------------------------------------------------------------

    /// <summary>Un carácter del PDF con lo que hace falta para agrupar el texto.</summary>
    internal sealed record CaracterPdf(
        string Texto, double Ux, double Uy, double E, double F, double Adv,
        double X0, double Top, double X1, double Bottom, string? Fuente, double Size, int Color);

    private static List<CaracterPdf> Caracteres(Page page)
    {
        double alto = page.Height;
        var lista = new List<CaracterPdf>(page.Letters.Count);
        foreach (var l in page.Letters)
        {
            if (string.IsNullOrEmpty(l.Value)) continue;
            double sx = l.StartBaseLine.X, sy = l.StartBaseLine.Y;
            double vx = l.EndBaseLine.X - sx, vy = l.EndBaseLine.Y - sy;
            double n = Math.Sqrt(vx * vx + vy * vy);
            double ux, uy;
            if (n > 1e-9) { ux = vx / n; uy = vy / n; }
            else
            {
                // carácter sin avance (p. ej. un espacio de ancho cero): la dirección se saca de la orientación
                switch (l.TextOrientation)
                {
                    case TextOrientation.Rotate180: ux = -1; uy = 0; break;
                    case TextOrientation.Rotate90: ux = 0; uy = -1; break;
                    case TextOrientation.Rotate270: ux = 0; uy = 1; break;
                    default: ux = 1; uy = 0; break;
                }
            }
            double nx = -uy, ny = ux;                       // "arriba" del texto
            // La caja del carácter, como la de pdfminer: del descenso de la fuente a descenso + tamaño de letra
            var suelo = l.GlyphRectangleLoose.BottomLeft;
            double desc = (suelo.X - sx) * nx + (suelo.Y - sy) * ny;
            double tam = l.PointSize > 0 ? l.PointSize : l.FontSize;
            double adv = l.Width;
            double ax = sx + desc * nx, ay = sy + desc * ny;
            double[] xs = { ax, ax + adv * ux, ax + tam * nx, ax + adv * ux + tam * nx };
            double[] ys = { ay, ay + adv * uy, ay + tam * ny, ay + adv * uy + tam * ny };
            double x0 = xs.Min(), x1 = xs.Max();
            double top = alto - ys.Max(), bottom = alto - ys.Min();
            lista.Add(new CaracterPdf(l.Value, ux, uy, sx, sy, adv, x0, top, x1, bottom,
                l.FontName, tam, ColorEntero(l.FillColor ?? l.Color)));
        }
        return lista;
    }

    /// <summary>Un trozo de texto (mismo estilo) dentro de una línea.</summary>
    internal sealed class Trozo
    {
        public string Texto = "";
        public double X0, Y0, X1, Y1;
        public double Tam;
        public int Color;
        public (string? Fuente, double Tam, int Color) Clave;
    }

    /// <summary>
    /// Une los caracteres en líneas de trozos. Una línea continúa mientras el siguiente carácter
    /// siga en la misma dirección y la misma línea de base, a una distancia razonable. Un trozo
    /// cambia cuando cambia la fuente, el tamaño o el color.
    /// </summary>
    internal static List<(Direccion Dir, List<Trozo> Trozos)> AgruparTexto(IReadOnlyList<CaracterPdf> chars)
    {
        var lineas = new List<(Direccion, List<Trozo>)>();
        List<Trozo>? trozosActuales = null;
        Direccion dirActual = default;
        bool hayUltimo = false;
        Direccion uDir = default;
        double uE = 0, uF = 0, uAdv = 0;

        foreach (var c in chars)
        {
            var d = Direccion.Redondeada(c.Ux, -c.Uy);
            double x0 = c.X0, y0 = c.Top, x1 = c.X1, y1 = c.Bottom;
            // El tamaño de letra es lo que mide el carácter de lado a lado de la línea.
            double tam; bool recto;
            if (d == Direccion.Horizontal || d == new Direccion(-1.0, 0.0)) { tam = y1 - y0; recto = true; }
            else if (d == Direccion.Vertical || d == new Direccion(0.0, 1.0)) { tam = x1 - x0; recto = true; }
            else { tam = c.Size; recto = false; }
            int color = c.Color;
            var clave = (c.Fuente, recto ? Math.Round(tam, 1, MidpointRounding.ToEven) : 0.0, color);

            bool seguir = false;
            double hueco = 0;
            if (trozosActuales != null && hayUltimo && uDir == d)
            {
                double dx = c.E - uE, dy = c.F - uF;
                double paso = dx * c.Ux + dy * c.Uy;                 // avance a lo largo de la línea
                double lado = -dx * c.Uy + dy * c.Ux;                // desvío de la línea de base
                hueco = paso - uAdv;
                seguir = Math.Abs(lado) <= 0.4 * tam && -0.6 * tam <= hueco && hueco <= HuecoMax * tam;
            }
            string texto = c.Texto;
            if (seguir)
            {
                var trozos = trozosActuales!;
                var ultimoTrozo = trozos[^1];
                if (hueco > Espacio * tam && !EsEspacio(texto) && !ultimoTrozo.Texto.EndsWith(' '))
                    ultimoTrozo.Texto += " ";                       // el PDF separa las palabras con un hueco, sin espacio
                if (ultimoTrozo.Clave == clave)
                {
                    ultimoTrozo.Texto += texto;
                    ultimoTrozo.X0 = Math.Min(ultimoTrozo.X0, x0);
                    ultimoTrozo.Y0 = Math.Min(ultimoTrozo.Y0, y0);
                    ultimoTrozo.X1 = Math.Max(ultimoTrozo.X1, x1);
                    ultimoTrozo.Y1 = Math.Max(ultimoTrozo.Y1, y1);
                }
                else
                {
                    trozos.Add(NuevoTrozo(texto, x0, y0, x1, y1, tam, color, clave));
                }
            }
            else
            {
                trozosActuales = new List<Trozo> { NuevoTrozo(texto, x0, y0, x1, y1, tam, color, clave) };
                dirActual = d;
                lineas.Add((dirActual, trozosActuales));
            }
            hayUltimo = true; uDir = d; uE = c.E; uF = c.F; uAdv = c.Adv;
        }
        return lineas;
    }

    private static bool EsEspacio(string s) => s.Length > 0 && s.All(char.IsWhiteSpace);

    private static Trozo NuevoTrozo(string texto, double x0, double y0, double x1, double y1, double tam, int color,
                                    (string?, double, int) clave) =>
        new() { Texto = texto, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Tam = tam, Color = color, Clave = clave };

    // ----------------------------------------------------------------------
    // Colores
    // ----------------------------------------------------------------------

    /// <summary>Color del PDF -> (r, g, b) de 0 a 1, o null si no se puede (patrones).</summary>
    private static Rgb? ColorRgb(IColor? c)
    {
        if (c == null) return null;
        try
        {
            var (r, g, b) = c.ToRGBValues();
            return new Rgb(r, g, b);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Color como entero 0xRRGGBB (negro si no se indica ninguno).</summary>
    private static int ColorEntero(IColor? c)
    {
        var rgb = ColorRgb(c) ?? new Rgb(0, 0, 0);
        static int Canal(double v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255, MidpointRounding.ToEven)));
        return (Canal(rgb.R) << 16) | (Canal(rgb.G) << 8) | Canal(rgb.B);
    }

    // ----------------------------------------------------------------------
    // Dibujo
    // ----------------------------------------------------------------------

    /// <summary>Un segmento de un trazado: 'l' recta (con sus dos puntos) o 'c' curva.</summary>
    private readonly record struct Seg(char Tipo, double X0, double Y0, double X1, double Y1);

    /// <summary>Trazado de un subpath -> rectas y curvas (las rectas de longitud cero no cuentan; el cierre sí).</summary>
    private static List<Seg> Segmentos(PdfSubpath sub, double alto, List<(double X, double Y)> puntos, out bool esRect)
    {
        var items = new List<Seg>();
        bool tieneActual = false, tieneInicio = false;
        double ax = 0, ay = 0, ix = 0, iy = 0;
        int lineas = 0; bool hayCurvas = false; bool cerrado = false;
        var pts = new List<(double X, double Y)>();   // los puntos de las rectas, para detectar un rectángulo
        foreach (var cmd in sub.Commands)
        {
            switch (cmd)
            {
                case PdfSubpath.Move m:
                    ax = ix = m.Location.X; ay = iy = alto - m.Location.Y;
                    tieneActual = tieneInicio = true;
                    puntos.Add((ax, ay)); pts.Add((ax, ay));
                    break;
                case PdfSubpath.Line l:
                {
                    double x = l.To.X, y = alto - l.To.Y;
                    puntos.Add((x, y));
                    lineas++;
                    pts.Add((x, y));
                    if (tieneActual && (Math.Abs(ax - x) > 1e-3 || Math.Abs(ay - y) > 1e-3))
                        items.Add(new Seg('l', ax, ay, x, y));
                    ax = x; ay = y; tieneActual = true;
                    break;
                }
                case PdfSubpath.BezierCurve b:
                {
                    hayCurvas = true;
                    foreach (var p in PuntosCurva(b)) puntos.Add((p.X, alto - p.Y));
                    items.Add(new Seg('c', 0, 0, 0, 0));
                    ax = b.EndPoint.X; ay = alto - b.EndPoint.Y; tieneActual = true;
                    break;
                }
                case PdfSubpath.Close:
                    cerrado = true;
                    if (tieneActual && tieneInicio && (Math.Abs(ax - ix) > 1e-3 || Math.Abs(ay - iy) > 1e-3))
                    {
                        items.Add(new Seg('l', ax, ay, ix, iy));
                        ax = ix; ay = iy;
                    }
                    break;
            }
        }

        // ¿pdfminer lo habría guardado como rectángulo (LTRect)? "mlllh" o "mllll" cerrado y con lados paralelos a los ejes
        esRect = false;
        if (!hayCurvas && ((lineas == 3 && cerrado) || (lineas == 4 && !cerrado)) && pts.Count == lineas + 1)
        {
            var (x0, y0) = pts[0]; var (x1, y1) = pts[1]; var (x2, y2) = pts[2]; var (x3, y3) = pts[3];
            bool cierra = lineas == 3 || (Math.Abs(pts[4].X - x0) < 1e-9 && Math.Abs(pts[4].Y - y0) < 1e-9);
            bool cuadrado = (x0 == x1 && y1 == y2 && x2 == x3 && y3 == y0) || (y0 == y1 && x1 == x2 && y2 == y3 && x3 == x0);
            esRect = cierra && cuadrado;
        }
        return items;
    }

    /// <summary>
    /// Puntos de una curva para medir su tamaño: solo el principio y el final. (Así no depende de si la versión de
    /// PdfPig distingue curvas cúbicas y cuadráticas; para los circulitos de las bornas el resultado es el mismo.)
    /// </summary>
    private static IEnumerable<PdfPoint> PuntosCurva(PdfSubpath.BezierCurve b)
    {
        yield return b.StartPoint;
        yield return b.EndPoint;
    }

    private static bool Cerca(double px, double py, double qx, double qy) => Math.Abs(px - qx) < 0.5 && Math.Abs(py - qy) < 0.5;

    /// <summary>Líneas rectas, flechas y circulitos de la página, para seguir cada cable hasta sus extremos.</summary>
    private static void LeerDibujo(Page page, Pagina pag, double esc, double alto)
    {
        foreach (var path in page.Paths)
        {
            if (path.IsClipping && !path.IsStroked && !path.IsFilled) continue;
            if (!path.IsStroked && !path.IsFilled) continue;

            Rgb? trazo = path.IsStroked ? ColorRgb(path.StrokeColor ?? GrayColor.Black) : null;
            Rgb? relleno = path.IsFilled ? ColorRgb(path.FillColor ?? GrayColor.Black) : null;
            Rgb? color = trazo?.Redondeado();
            Rgb? rellenoR = relleno?.Redondeado();

            foreach (var sub in path)
            {
                var puntos = new List<(double X, double Y)>();
                var items = Segmentos(sub, alto, puntos, out bool esRect);
                if (esRect && !RectangulosComoTrazos) continue;
                if (items.Count == 0 && puntos.Count == 0) continue;

                if (rellenoR != null && items.Count == 3 && items.All(i => i.Tipo == 'l'))
                {
                    // triángulo relleno = flecha de referencia a otra hoja
                    double mx = items.Sum(i => i.X0) / 3 * esc;
                    double my = items.Sum(i => i.Y0) / 3 * esc;
                    pag.Flechas.Add(new Flecha(mx, my, rellenoR.Value));
                    continue;
                }
                if (color == null) continue;
                if (items.Count == 4 && items.All(i => i.Tipo == 'l') && Cerca(items[^1].X1, items[^1].Y1, items[0].X0, items[0].Y0))
                    continue;     // un cuadrilátero cerrado (marco o recuadro, no una línea de cable)
                if (items.Count > 0 && items.All(i => i.Tipo == 'c'))
                {
                    double x0 = puntos.Min(p => p.X), x1 = puntos.Max(p => p.X);
                    double top = puntos.Min(p => p.Y), bottom = puntos.Max(p => p.Y);
                    if (x1 - x0 <= 9 && bottom - top <= 9)
                    {
                        pag.Circulos.Add(new Circulo(x0 * esc, top * esc, x1 * esc, bottom * esc));
                        continue;
                    }
                }
                foreach (var it in items)
                    if (it.Tipo == 'l')
                        pag.Trazos.Add(new Trazo(it.X0 * esc, it.Y0 * esc, it.X1 * esc, it.Y1 * esc, color.Value));
            }
        }
    }
}
