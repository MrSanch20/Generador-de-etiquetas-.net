namespace Etiquetador.Core;

/// <summary>
/// Motor de reconocimiento de texto (OCR) que se puede enchufar al programa. La versión Python usa RapidOCR;
/// en esta versión .NET el motor NO está incluido (ver docs/NOTAS-PORT.md). Sin motor, los PDF escaneados
/// y las imágenes dan un aviso claro en lugar de leerse.
/// </summary>
public interface IMotorOcr
{
    /// <summary>Lee una imagen (PNG, JPG, TIFF…) y devuelve sus páginas, con <c>Origen = "ocr"</c>.</summary>
    List<Pagina> LeerImagen(string ruta, Action<string>? progreso);

    /// <summary>Renderiza y lee la página <paramref name="numero"/> (1..n) de un PDF escaneado.</summary>
    Pagina LeerPaginaPdf(string ruta, int numero, Action<string>? progreso);
}

public static class LectorOcr
{
    /// <summary>Motor de OCR instalado (o null si no hay ninguno).</summary>
    public static IMotorOcr? Motor { get; set; }

    public static bool Disponible => Motor != null;
    public static bool PuedeLeerPaginaPdf => Motor != null;

    private const string SinOcr =
        "El reconocimiento de texto (OCR) no está incluido en esta versión .NET.\n" +
        "Usa un PDF con texto (el que genera el programa de esquemas), un DXF o un listado Excel/CSV.";

    public static List<Pagina> LeerImagen(string ruta, Action<string>? progreso = null)
    {
        if (Motor == null) throw new ErrorLectura(SinOcr);
        return Motor.LeerImagen(ruta, progreso);
    }

    public static Pagina PaginaPdf(string ruta, int numero, Action<string>? progreso = null)
    {
        if (Motor == null) throw new ErrorLectura(SinOcr);
        return Motor.LeerPaginaPdf(ruta, numero, progreso);
    }
}
