namespace Etiquetador.Core.Modelos;

/// <summary>Un texto con su posición. Coordenadas con el origen arriba a la izquierda.</summary>
public sealed class TextoPos
{
    public string Texto { get; init; } = "";
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public Direccion Dir { get; init; } = Direccion.Horizontal;
    /// <summary>Tamaño de letra.</summary>
    public double Tam { get; init; }
    /// <summary>Lo que distingue el estilo del texto: color (int 0xRRGGBB) en PDF; (alto, capa, color) en DXF; "ocr" en OCR.</summary>
    public object? Estilo { get; init; }

    public TextoPos() { }

    public TextoPos(string texto, double x0, double y0, double x1, double y1, Direccion dir, double tam, object? estilo)
    {
        Texto = texto; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Dir = dir; Tam = tam; Estilo = estilo;
    }

    /// <summary>¿El estilo es el color entero indicado? (solo PDF)</summary>
    public bool EsColor(int color) => Estilo is int c && c == color;
}
