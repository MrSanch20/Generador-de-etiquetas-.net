namespace Etiquetador.Core.Modelos;

/// <summary>Un hilo del esquema (cable) con su número y color.</summary>
public sealed class Cable : IFila
{
    public string Numero { get; set; } = "";
    public string Color { get; set; } = "";
    /// <summary>"Fuerza" o "Maniobra" ("" si no tiene número).</summary>
    public string Tipo { get; set; } = "";
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public bool Dudoso { get; set; }
    public int Pagina { get; set; }
    public string Esquema { get; set; } = "";
    public int Orden { get; set; }
    /// <summary>"1/2" o "2/2" (una etiqueta para cada punta); "" si aún no se ha separado por extremos.</summary>
    public string Extremo { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public Cable Clonar() => (Cable)MemberwiseClone();
    public IFila Duplicar() => Clonar();
}
