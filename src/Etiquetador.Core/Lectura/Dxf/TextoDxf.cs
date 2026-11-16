namespace Etiquetador.Core.Lectura.Dxf;

// Un texto del dibujo con su posición, giro y tamaño ya en coordenadas del dibujo.
internal sealed record TextoDxf(string Texto, double X, double Y, double Rot, double Alto, object Estilo, double Ancho);

internal readonly record struct Transf(double Ix, double Iy, double Sx, double Sy, double RotGrados, double Bx, double By)
{
    public static readonly Transf Identidad = new(0, 0, 1, 1, 0, 0, 0);

    public (double X, double Y) Punto(double x, double y)
    {
        double a = RotGrados * Math.PI / 180;
        double dx = (x - Bx) * Sx, dy = (y - By) * Sy;
        return (Ix + dx * Math.Cos(a) - dy * Math.Sin(a), Iy + dx * Math.Sin(a) + dy * Math.Cos(a));
    }
}
