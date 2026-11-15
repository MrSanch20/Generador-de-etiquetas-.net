using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>
/// Cables (hilos): cada hilo lleva una marca inclinada en el extremo con su número (negro) y debajo su color (rojo): "L01 / NE".
/// Con el dibujo vectorial del PDF se sigue la línea hasta sus dos extremos (borna de un aparato, regleta, flecha a otra hoja…).
/// </summary>
public static class ExtractorCables
{
    // Cables (hilos)
    // En el esquema cada hilo lleva una marca inclinada en el extremo; encima su número (negro) y debajo su
    // color (rojo): "L01 / NE", "060 / BL"… Con el dibujo vectorial del PDF se sigue la línea del hilo hasta
    // sus dos extremos (borna de un aparato, regleta, flecha de referencia a otra hoja…).
    private const string Colores = "NE|MA|AZ|GR|BL|VE|AM|RS|BU|NA|RO|BN|BK|GN|YE|RD|WH|OR|VI|GY|PK";

    public static readonly Regex ColorCableRe = Rx.Crear($"(?:{Colores})(?:/(?:{Colores}))?");

    // Hilos de fuerza (potencia): fases, neutro, tierra, salida a motor (L1, L01, N, PE, U0…).
    public static readonly Regex FuerzaRe = Rx.Crear(@"(?:[LN]\d{0,3}|PE\d*|[RSTUVW]\d{0,3}|\d?[LT]\d)", RegexOptions.IgnoreCase);

    private static readonly Regex DesignacionRe = Rx.Crear(@"(?:\+?-)?[A-Z][A-Z_]*\d*(?:\.\d+)?");

    /// <summary>'Fuerza' si el número de hilo es de potencia (L1, L01, N, PE, U0, V0…); si no, 'Maniobra'.</summary>
    public static string TipoCable(string numero) => Rx.Completa(FuerzaRe, numero.Trim()) ? "Fuerza" : "Maniobra";

    /// <summary>Color entero de un texto (0xRRGGBB) -> (r, g, b) de 0 a 1.</summary>
    private static Rgb RgbDe(int c) => new(
        Math.Round(((c >> 16) & 255) / 255.0, 2, MidpointRounding.ToEven),
        Math.Round(((c >> 8) & 255) / 255.0, 2, MidpointRounding.ToEven),
        Math.Round((c & 255) / 255.0, 2, MidpointRounding.ToEven));

    private sealed record Marco(double X0, double Y0, double X1, double Y1, string Nombre);

    /// <summary>Recuadros azules (los aparatos dibujados como caja) con el nombre que tienen a su izquierda.</summary>
    private static List<Marco> Marcos(Pagina pag, List<TextoPos> designaciones)
    {
        var azules = pag.Trazos.Where(s => s.Color == SeguimientoDeHilos.Azul && (Math.Abs(s.X0 - s.X1) < 0.3 || Math.Abs(s.Y0 - s.Y1) < 0.3)).ToList();
        var hor = azules.Where(s => Math.Abs(s.Y0 - s.Y1) < 0.3 && Math.Abs(s.X0 - s.X1) > 20)
                        .Select(s => (X0: Math.Min(s.X0, s.X1), X1: Math.Max(s.X0, s.X1), Y: s.Y0)).ToList();
        var ver = azules.Where(s => Math.Abs(s.X0 - s.X1) < 0.3 && Math.Abs(s.Y0 - s.Y1) > 20)
                        .Select(s => (X: s.X0, Y0: Math.Min(s.Y0, s.Y1), Y1: Math.Max(s.Y0, s.Y1))).ToList();
        var cajas = new List<(double X0, double Y0, double X1, double Y1)>();
        for (int i = 0; i < hor.Count; i++)
        {
            var (ax0, ax1, ay) = hor[i];
            for (int j = i + 1; j < hor.Count; j++)
            {
                var (bx0, bx1, by) = hor[j];
                if (Math.Abs(ax0 - bx0) > 0.6 || Math.Abs(ax1 - bx1) > 0.6 || Math.Abs(by - ay) < 20) continue;
                double y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
                bool izq = ver.Any(v => Math.Abs(v.X - ax0) <= 0.6 && v.Y0 <= y0 + 0.6 && v.Y1 >= y1 - 0.6);
                bool der = ver.Any(v => Math.Abs(v.X - ax1) <= 0.6 && v.Y0 <= y0 + 0.6 && v.Y1 >= y1 - 0.6);
                if (izq && der) cajas.Add((ax0, y0, ax1, y1));
            }
        }
        var res = new List<Marco>();
        foreach (var (x0, y0, x1, y1) in cajas)
        {
            if ((x1 - x0) * (y1 - y0) > pag.Ancho * pag.Alto * 0.5) continue;
            // nombre: justo a la izquierda del borde izquierdo, a la altura de la parte alta
            var cand = designaciones.Where(t => t.X1 <= x0 + 12 && t.X1 >= x0 - 80 && y0 - 15 <= t.Y0 && t.Y0 <= y0 + 40).ToList();
            if (cand.Count > 0)
            {
                var t = cand.MinBy(t => Math.Abs(t.Y0 - y0) + Math.Abs(x0 - t.X1))!;
                res.Add(new Marco(x0, y0, x1, y1, t.Texto.TrimStart('+', '-')));
            }
        }
        return res;
    }

    /// <summary>Nombre del aparato al que pertenece el punto: el recuadro con nombre más grande que lo contiene;
    /// si no hay, el nombre más cercano a la izquierda y arriba (o en la misma fila).</summary>
    private static string Dueno(List<Marco> marcos, List<TextoPos> designaciones, double cx, double cy)
    {
        var dentro = marcos.Where(m => m.X0 - 1 <= cx && cx <= m.X1 + 1 && m.Y0 - 1 <= cy && cy <= m.Y1 + 1).ToList();
        if (dentro.Count > 0) return dentro.MaxBy(m => (m.X1 - m.X0) * (m.Y1 - m.Y0))!.Nombre;
        var cerca = designaciones.Where(t => t.X0 <= cx && t.Y0 <= cy + 15 && SeguimientoDeHilos.DistTexto(cx, cy, t) <= 160).ToList();
        return cerca.Count > 0 ? cerca.MinBy(t => SeguimientoDeHilos.DistTexto(cx, cy, t))!.Texto.TrimStart('+', '-') : "";
    }

    private static bool EstiloEs(TextoPos t, int color) => t.Estilo is int c && c == color;

    /// <summary>Texto de un extremo del hilo. Devuelve (texto, dudoso).</summary>
    private static (string Texto, bool Dudoso) Extremo(
        Pagina pag, (double X, double Y) junta, (double X, double Y) punto, (double X, double Y) sentido,
        List<TextoPos> pines, List<TextoPos> designaciones, List<Marco> marcos)
    {
        double px = junta.X, py = junta.Y;
        // 1) flecha de referencia a otra hoja: "101 (hoja 93.7)"
        var cerca = pag.Flechas.Where(f => SeguimientoDeHilos.Hypot(f.X - px, f.Y - py) <= 9).ToList();
        if (cerca.Count > 0)
        {
            double fx = cerca[0].X, fy = cerca[0].Y;
            var grandes = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal && EstiloEs(t, 0x0000FF) && t.Tam >= 9 && SeguimientoDeHilos.DistTexto(fx, fy, t) <= 30).ToList();
            var hojas = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal && EstiloEs(t, 0x7CA552) && SeguimientoDeHilos.DistTexto(fx, fy, t) <= 30).ToList();
            string nombre = grandes.Count > 0 ? grandes.MinBy(t => SeguimientoDeHilos.DistTexto(fx, fy, t))!.Texto : "";
            string hoja = hojas.Count > 0 ? hojas.MinBy(t => SeguimientoDeHilos.DistTexto(fx, fy, t))!.Texto.Trim('/', ' ') : "";
            if (nombre.Length > 0 || hoja.Length > 0)
                return (nombre.Length > 0 && hoja.Length > 0 ? $"{nombre} (hoja {hoja})" : (nombre.Length > 0 ? nombre : $"hoja {hoja}"), false);
        }
        // 2) borna: número de pin junto al extremo, más allá de donde acaba la línea
        double qx = punto.X + sentido.X * 6, qy = punto.Y + sentido.Y * 6;
        double Lejos(TextoPos t) => Math.Min(Math.Min(SeguimientoDeHilos.DistTexto(px, py, t), SeguimientoDeHilos.DistTexto(punto.X, punto.Y, t)), SeguimientoDeHilos.DistTexto(qx, qy, t));
        var pinesCerca = pines.Where(t => Lejos(t) <= 14).ToList();
        TextoPos? pin = pinesCerca.Count > 0 ? pinesCerca.MinBy(Lejos) : null;
        double cx = pin != null ? (pin.X0 + pin.X1) / 2 : px;
        double cy = pin != null ? (pin.Y0 + pin.Y1) / 2 : py;
        string dueno = Dueno(marcos, designaciones, cx, cy);
        if (pin != null && dueno.Length > 0) return ($"{dueno}:{pin.Texto}", false);
        if (pin != null) return (pin.Texto, true);
        return dueno.Length > 0 ? (dueno, true) : ("", true);
    }

    /// <summary>
    /// Hilos del esquema. Solo en PDF con texto (hace falta el dibujo vectorial). Los hilos sin número
    /// (solo color, p. ej. las tierras) salen con número "".
    /// </summary>
    public static List<Cable> Extraer(IEnumerable<Pagina> paginas)
    {
        var filas = new List<(Cable Cable, double Y, double X)>();
        var vistos = new HashSet<(int, string, string, string, string)>();
        foreach (var pag in paginas)
        {
            if (pag.Origen != "pdf" || pag.Trazos.Count == 0) continue;
            var trazos = pag.Trazos;
            var horizontales = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal).ToList();
            var pines = horizontales.Where(t => 7.0 <= t.Tam && t.Tam <= 8.2 && EstiloEs(t, 0) && (t.Y0 + t.Y1) / 2 < pag.Alto * Medidas.Pie).ToList();
            var designaciones = horizontales.Where(t => EstiloEs(t, 0x0000FF) && t.Tam >= 9 && t.Texto.Length <= 9
                                    && Rx.Completa(DesignacionRe, t.Texto)
                                    && (t.Texto[0] == '+' || t.Texto[0] == '-' || t.Texto[0] == 'X' || t.Texto.Any(char.IsDigit))).ToList();
            designaciones = designaciones.Where(t => !pag.Flechas.Any(f => SeguimientoDeHilos.DistTexto(f.X, f.Y, t) <= 30)).ToList();
            var marcos = Marcos(pag, designaciones);
            // marcas inclinadas: líneas cortas en diagonal
            var marcas = new List<int>();
            for (int i = 0; i < trazos.Count; i++)
            {
                var s = trazos[i];
                double len = SeguimientoDeHilos.Hypot(s.X1 - s.X0, s.Y1 - s.Y0);
                double ratio = Math.Abs(s.X1 - s.X0) / Math.Max(Math.Abs(s.Y1 - s.Y0), 0.01);
                if (5 <= len && len <= 11 && 0.6 <= ratio && ratio <= 1.6) marcas.Add(i);
            }
            var marcasSet = marcas.ToHashSet();
            foreach (var ct in horizontales)
            {
                if (!Rx.Completa(ColorCableRe, ct.Texto) || !(7.0 <= ct.Tam && ct.Tam <= 10.5)) continue;
                if ((ct.Y0 + ct.Y1) / 2 > pag.Alto * Medidas.Pie) continue;
                Rgb? colRgb = ct.Estilo is int ci ? RgbDe(ci) : null;
                // número del hilo: texto justo encima del color, en la misma columna
                var encima = horizontales.Where(t => !ReferenceEquals(t, ct) && Math.Abs(t.Tam - ct.Tam) <= 0.3 && !Equals(t.Estilo, ct.Estilo)
                                                    && Math.Abs(t.X0 - ct.X0) <= 3 && 0 < ct.Y0 - t.Y0 && ct.Y0 - t.Y0 <= ct.Tam * 1.7).ToList();
                string numero = encima.Count > 0 ? encima.MinBy(t => ct.Y0 - t.Y0)!.Texto : "";
                // marca inclinada a la izquierda del color
                double mx = ct.X0 - 2, my = (ct.Y0 + ct.Y1) / 2 + 3;
                var tick = marcas.Where(i => colRgb != null && trazos[i].Color == colRgb.Value
                                              && SeguimientoDeHilos.Hypot((trazos[i].X0 + trazos[i].X1) / 2 - mx, (trazos[i].Y0 + trazos[i].Y1) / 2 - my) <= 12).ToList();
                string origen = "", destino = "";
                bool dudoso = true;
                if (tick.Count > 0)
                {
                    var t = trazos[tick[0]];
                    double tx = (t.X0 + t.X1) / 2, ty = (t.Y0 + t.Y1) / 2;
                    var hilos = new List<int>();
                    for (int i = 0; i < trazos.Count; i++)
                    {
                        var s = trazos[i];
                        if (marcasSet.Contains(i)) continue;
                        if (SeguimientoDeHilos.Hypot(s.X1 - s.X0, s.Y1 - s.Y0) <= 12) continue;
                        if (!(Math.Abs(s.X0 - s.X1) < 0.3 || Math.Abs(s.Y0 - s.Y1) < 0.3)) continue;
                        if (SeguimientoDeHilos.DistPuntoSeg(tx, ty, s) <= 1.0) hilos.Add(i);
                    }
                    if (hilos.Count > 0)
                    {
                        // el hilo no es azul (el azul es el dibujo de los aparatos y los subrayados de texto)
                        int h = hilos.OrderBy(i => trazos[i].Color == SeguimientoDeHilos.Azul ? 1 : 0).ThenBy(i => SeguimientoDeHilos.DistPuntoSeg(tx, ty, trazos[i])).First();
                        var extremos = new List<(double Y, double X, string Texto, bool Dud)>();
                        for (int lado = 0; lado < 2; lado++)
                        {
                            var (punto, empalme) = SeguimientoDeHilos.Seguir(trazos, pag.Circulos, h, lado);
                            var s = trazos[h];
                            (double X, double Y) otro = lado == 0 ? (s.X1, s.Y1) : (s.X0, s.Y0);
                            // sentido de salida: desde el interior del hilo hacia el extremo
                            double vx = punto.X - otro.X, vy = punto.Y - otro.Y;
                            double n = SeguimientoDeHilos.Hypot(vx, vy);
                            if (n == 0) n = 1;
                            var sentido = (vx / n, vy / n);
                            var punta = SeguimientoDeHilos.Prolongar(trazos, punto, sentido);
                            var (texto, dud) = Extremo(pag, punto, punta, sentido, pines, designaciones, marcos);
                            extremos.Add((punto.Y, punto.X, texto, dud || empalme));
                        }
                        extremos = extremos.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Texto, StringComparer.Ordinal).ThenBy(e => e.Dud).ToList();
                        origen = extremos[0].Texto; destino = extremos[1].Texto;
                        dudoso = extremos[0].Dud || extremos[1].Dud || origen.Length == 0 || destino.Length == 0;
                    }
                }
                if (numero.Length > 0)
                {
                    var clave = (pag.Numero, numero, ct.Texto, origen, destino);
                    if (!vistos.Add(clave)) continue;
                }
                filas.Add((new Cable
                {
                    Numero = numero, Color = ct.Texto, Tipo = numero.Length > 0 ? TipoCable(numero) : "",
                    Origen = origen, Destino = destino, Dudoso = dudoso, Pagina = pag.Numero,
                }, ct.Y0, ct.X0));
            }
        }
        // Un hilo con número "raro" (los hilos 1, 2, 3 de la manguera de un motor) que llega al mismo extremo
        // (regleta:borna) que un hilo de fuerza (U0, V0, W0…) también es de fuerza.
        var extremosFuerza = new HashSet<(int, string)>();
        foreach (var (f, _, _) in filas)
            if (f.Tipo == "Fuerza")
                foreach (var e in new[] { f.Origen, f.Destino })
                    if (e.Contains(':')) extremosFuerza.Add((f.Pagina, e));
        foreach (var (f, _, _) in filas)
            if (f.Tipo == "Maniobra" && new[] { f.Origen, f.Destino }.Any(e => e.Contains(':') && extremosFuerza.Contains((f.Pagina, e))))
                f.Tipo = "Fuerza";
        // Orden de lectura: página, de arriba abajo (por líneas) y de izquierda a derecha
        return filas.OrderBy(f => f.Cable.Pagina).ThenBy(f => Redondeo.R(f.Y / Medidas.Linea)).ThenBy(f => f.X).Select(f => f.Cable).ToList();
    }
}
