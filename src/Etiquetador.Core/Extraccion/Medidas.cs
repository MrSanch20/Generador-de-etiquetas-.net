
namespace Etiquetador.Core.Extraccion;

/// <summary>Medidas (en puntos del PDF) que usan los extractores para decidir qué texto va con cuál.</summary>
public static class Medidas
{
    public const double MaxDistDescripcion = 50;   // distancia máx. borna <-> texto descriptivo
    public const double Linea = 15;                // alto (pt) que se considera "la misma línea" al ordenar
    public const double GapFila = 30;              // separación vertical (pt) mínima entre dos filas de carriles de la placa
    public const double Pie = 0.93;                // por debajo de esta altura (fracción) está el cajetín
}
