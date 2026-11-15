namespace Etiquetador.Core.Modelos;

public sealed class Pagina
{
    public int Numero { get; }
    public double Ancho { get; }
    public double Alto { get; }
    /// <summary>Textos agrupados por línea.</summary>
    public List<TextoPos> Lineas { get; } = new();
    /// <summary>Textos sueltos (spans).</summary>
    public List<TextoPos> Trozos { get; } = new();
    /// <summary>pdf | dxf | ocr</summary>
    public string Origen { get; set; } = "pdf";
    /// <summary>Página escaneada que no se ha podido leer (falta el OCR).</summary>
    public bool SinOcr { get; set; }
    // Dibujo vectorial (solo PDF con texto; sirve para seguir los cables hasta sus extremos).
    public List<Trazo> Trazos { get; } = new();
    public List<Flecha> Flechas { get; } = new();
    public List<Circulo> Circulos { get; } = new();

    public Pagina(int numero, double ancho, double alto, string origen = "pdf")
    {
        Numero = numero; Ancho = ancho; Alto = alto; Origen = origen;
    }

    /// <summary>Todo el texto cuyo centro está entre dos alturas, unido con espacios.</summary>
    public string TextoZona(double yDesde = 0.0, double? yHasta = null)
    {
        double hasta = yHasta ?? Alto;
        return string.Join(" ", Trozos.Where(t => yDesde <= (t.Y0 + t.Y1) / 2 && (t.Y0 + t.Y1) / 2 <= hasta).Select(t => t.Texto));
    }
}
