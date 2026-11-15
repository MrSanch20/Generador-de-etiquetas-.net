namespace Etiquetador.Core.Modelos;

/// <summary>Un elemento del esquema (KM100, Q100…).</summary>
public sealed class Elemento : IFila
{
    public string Designacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    /// <summary>Páginas (int) o filas ("fila N") donde aparece.</summary>
    public List<object> Paginas { get; set; } = new();
    public string Esquema { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public string PaginasTexto => string.Join(", ", Paginas.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));

    public Elemento Clonar()
    {
        var e = (Elemento)MemberwiseClone();
        e.Paginas = new List<object>(Paginas);
        return e;
    }
    public IFila Duplicar() => Clonar();
}
