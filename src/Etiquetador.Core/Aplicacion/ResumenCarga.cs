namespace Etiquetador.Core.Aplicacion;

/// <summary>Resumen de una carga para enseñárselo al usuario.</summary>
public sealed class ResumenCarga
{
    public List<string> Errores { get; } = new();
    public List<string> AvisosOcr { get; } = new();
    public List<string> AvisosSinOcr { get; } = new();
    public List<string> Resumen { get; } = new();
    public int SinNumero { get; set; }
    public int PaginasSinOcr { get; set; }
    public bool HayFilasNuevas { get; set; }
    /// <summary>Texto para la barra de estado (null si no hay nada que decir).</summary>
    public string? Estado { get; set; }
}
