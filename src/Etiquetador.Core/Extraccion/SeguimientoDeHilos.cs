using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Sigue la línea de un hilo por el dibujo vectorial de la página hasta sus extremos.</summary>
public static class SeguimientoDeHilos
{
    internal const double TolPunto = 0.8;     // pt: dos extremos de línea a esta distancia son el mismo punto
    internal const int MaxPasos = 400;        // tope de segmentos al seguir un hilo (evita bucles)
    internal const double MaxHueco = 9.0;     // pt: hueco máximo entre dos trazos de una línea discontinua

    internal static readonly Rgb Azul = new(0.0, 0.0, 1.0);

    internal static double DistPuntoSeg(double px, double py, Trazo s)
    {
        double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
        double n = dx * dx + dy * dy;
        double t = n == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - s.X0) * dx + (py - s.Y0) * dy) / n));
        return Math.Sqrt(Sq(px - (s.X0 + t * dx)) + Sq(py - (s.Y0 + t * dy)));
    }

    internal static double Sq(double v) => v * v;

    internal static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    /// <summary>Distancia de un punto a una caja.</summary>
    internal static double DistCaja((double X, double Y) p, Circulo c) =>
        Hypot(Math.Max(Math.Max(c.X0 - p.X, 0), p.X - c.X1), Math.Max(Math.Max(c.Y0 - p.Y, 0), p.Y - c.Y1));

    /// <summary>Si el hilo acaba en un patillo de otro color (el terminal del aparato), sigue hasta su punta.</summary>
    internal static (double X, double Y) Prolongar(List<Trazo> trazos, (double X, double Y) punto, (double X, double Y) sentido)
    {
        for (int rep = 0; rep < 3; rep++)
        {
            (double N, (double, double) P)? mejor = null;
            foreach (var o in trazos)
            {
                for (int ext = 0; ext < 2; ext++)
                {
                    double ex = ext == 0 ? o.X0 : o.X1, ey = ext == 0 ? o.Y0 : o.Y1;
                    if (Hypot(ex - punto.X, ey - punto.Y) > TolPunto) continue;
                    double fx = ext == 0 ? o.X1 : o.X0, fy = ext == 0 ? o.Y1 : o.Y0;
                    double vx = fx - ex, vy = fy - ey;
                    double n = Hypot(vx, vy);
                    if (!(1 < n && n <= 30)) continue;
                    if ((vx * sentido.X + vy * sentido.Y) / n < 0.97) continue;     // debe seguir en la misma dirección
                    if (mejor == null || n > mejor.Value.N) mejor = (n, (fx, fy));
                }
            }
            if (mejor == null) break;
            punto = mejor.Value.P;
        }
        return punto;
    }

    /// <summary>Distancia de un punto al rectángulo de un texto.</summary>
    internal static double DistTexto(double px, double py, TextoPos t)
    {
        double dx = Math.Max(Math.Max(t.X0 - px, 0), px - t.X1);
        double dy = Math.Max(Math.Max(t.Y0 - py, 0), py - t.Y1);
        return Hypot(dx, dy);
    }

    /// <summary>Sigue un hilo desde un extremo de su segmento. Devuelve (punto final, empalme).</summary>
    internal static ((double X, double Y) Punto, bool Empalme) Seguir(List<Trazo> trazos, List<Circulo> circulos, int inicio, int extremo)
    {
        var seg = trazos[inicio];
        var color = seg.Color;
        (double X, double Y) punto = extremo == 0 ? (seg.X0, seg.Y0) : (seg.X1, seg.Y1);
        var visto = new HashSet<int> { inicio };
        var segActual = seg;
        for (int paso = 0; paso < MaxPasos; paso++)
        {
            var vecinos = new List<int>();
            for (int i = 0; i < trazos.Count; i++)
            {
                var o = trazos[i];
                if (visto.Contains(i) || o.Color != color) continue;
                if (DistPuntoSeg(punto.X, punto.Y, o) <= TolPunto) vecinos.Add(i);
            }
            if (vecinos.Count > 1)
            {
                var largos = vecinos.Where(i => Hypot(trazos[i].X1 - trazos[i].X0, trazos[i].Y1 - trazos[i].Y0) > 6).ToList();
                if (largos.Count > 0 && largos.Count < vecinos.Count)
                {
                    foreach (var i in vecinos) if (!largos.Contains(i)) visto.Add(i);      // muescas de la esquina
                    vecinos = largos;
                }
            }
            if (vecinos.Count == 0)
            {
                if (circulos.Any(c => DistCaja(punto, c) <= 3.5))
                    return (punto, false);          // llega a una borna (circulito): aquí acaba el hilo
                // línea discontinua (manguera): se salta el hueco hasta el trazo siguiente, en la misma dirección
                var sig = SiguienteTrazo(trazos, visto, color, punto, segActual);
                if (sig == null) return (punto, false);
                visto.Add(sig.Value.Indice);
                segActual = trazos[sig.Value.Indice];
                punto = sig.Value.Otro;
                continue;
            }
            if (vecinos.Count > 1) return (punto, true);
            var oo = trazos[vecinos[0]];
            (double X, double Y) a = (oo.X0, oo.Y0), b = (oo.X1, oo.Y1);
            double da = Hypot(a.X - punto.X, a.Y - punto.Y), db = Hypot(b.X - punto.X, b.Y - punto.Y);
            if (Math.Min(da, db) > TolPunto) return (punto, true);     // el hilo toca el medio de otra línea (derivación en T)
            visto.Add(vecinos[0]);
            segActual = oo;
            punto = da <= db ? b : a;
        }
        return (punto, true);
    }

    /// <summary>Trazo del mismo color que continúa la línea (misma dirección) tras un hueco.</summary>
    internal static (int Indice, (double X, double Y) Otro)? SiguienteTrazo(
        List<Trazo> trazos, HashSet<int> visto, Rgb color, (double X, double Y) punto, Trazo seg)
    {
        double dx = punto.X - (seg.X0 + seg.X1) / 2, dy = punto.Y - (seg.Y0 + seg.Y1) / 2;
        double n = Hypot(dx, dy);
        if (n == 0) return null;
        dx /= n; dy /= n;
        (double D, int I, (double, double) P)? mejor = null;
        for (int i = 0; i < trazos.Count; i++)
        {
            var o = trazos[i];
            if (visto.Contains(i) || o.Color != color) continue;
            for (int ext = 0; ext < 2; ext++)
            {
                double ex = ext == 0 ? o.X0 : o.X1, ey = ext == 0 ? o.Y0 : o.Y1;
                double vx = ex - punto.X, vy = ey - punto.Y;
                double d = Hypot(vx, vy);
                if (d < 0.01 || d > MaxHueco) continue;
                if ((vx * dx + vy * dy) / d < 0.97) continue;        // no sigue la misma dirección
                if (mejor == null || d < mejor.Value.D)
                    mejor = (d, i, ext == 0 ? (o.X1, o.Y1) : (o.X0, o.Y0));
            }
        }
        return mejor == null ? null : (mejor.Value.I, mejor.Value.P);
    }
}
