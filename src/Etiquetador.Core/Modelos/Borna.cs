namespace Etiquetador.Core.Modelos;

/// <summary>Una borna de una regleta.</summary>
public sealed class Borna : IFila
{
    public string Regleta { get; set; } = "";
    public int Pos { get; set; }
    public string Marca { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>Número de página (int) o "fila N" (string) si viene de un listado.</summary>
    public object Pagina { get; set; } = 0;
    public string Esquema { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public Borna Clonar() => (Borna)MemberwiseClone();
    public IFila Duplicar() => Clonar();
}
