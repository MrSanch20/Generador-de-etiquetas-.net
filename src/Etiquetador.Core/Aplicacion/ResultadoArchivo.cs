namespace Etiquetador.Core.Aplicacion;

/// <summary>Lo que se ha leído de un archivo (para pasarlo luego a la sesión).</summary>
public sealed class ResultadoArchivo
{
    public string Ruta { get; init; } = "";
    public string Nombre { get; init; } = "";
    public List<Borna> Bornas { get; init; } = new();
    public List<Elemento> Elementos { get; init; } = new();
    public List<Cable> Cables { get; init; } = new();
    public List<int> Ocr { get; init; } = new();
    public List<int> SinOcr { get; init; } = new();
    public string? Error { get; init; }
}
