
namespace Etiquetador.Core.Utilidades;

/// <summary>Redondeos con la misma regla que Python (el .5 va al par), para que el resultado coincida.</summary>
public static class Redondeo
{
    public static int R(double v) => (int)Math.Round(v, MidpointRounding.ToEven);
}
