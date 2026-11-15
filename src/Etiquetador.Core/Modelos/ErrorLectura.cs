namespace Etiquetador.Core.Modelos;

/// <summary>Error con un mensaje pensado para el usuario.</summary>
public class ErrorLectura : Exception
{
    public ErrorLectura(string mensaje) : base(mensaje) { }
}
