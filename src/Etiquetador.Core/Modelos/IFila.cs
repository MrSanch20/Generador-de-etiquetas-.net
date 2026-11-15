namespace Etiquetador.Core.Modelos;

/// <summary>Fila de etiquetas que se puede repetir por esquema.</summary>
public interface IFila
{
    string Esquema { get; set; }
    string Copia { get; set; }
    string SwCode { get; set; }
    IFila Duplicar();
}
