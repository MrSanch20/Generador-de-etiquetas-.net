namespace Etiquetador.Core;

/// <summary>
/// Lectores de archivos.
///
/// Cada lector convierte un archivo (PDF, DXF/DWG, imagen escaneada) en una lista de páginas
/// con sus textos y posiciones, en un formato común. Así la lógica de bornas y elementos
/// (<see cref="Extraccion"/>) es la misma para todos los formatos.
///
/// Coordenadas: como en un PDF, con el origen arriba a la izquierda y la Y hacia abajo, y
/// escaladas para que el alto de página sea ~842 (un A3/A4 en puntos).
/// </summary>
public static class Lectores
{
    public const double AltoRef = 841.89;     // alto de página de referencia (puntos)

    public static readonly string[] ExtPdf = { ".pdf" };
    public static readonly string[] ExtDxf = { ".dxf" };
    public static readonly string[] ExtDwg = { ".dwg" };
    public static readonly string[] ExtImagen = { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp" };
    public static readonly string[] ExtTabla = { ".xlsx", ".xlsm", ".xls", ".csv", ".txt" };

    /// <summary>Formato admitido, para mostrarlo en la ventana.</summary>
    public sealed record Formato(string Nombre, string[] Extensiones, string Nota);

    public static readonly Formato[] Formatos =
    {
        new("PDF del programa de esquemas", ExtPdf, "El más fiable."),
        new("PDF escaneado o imagen", ExtPdf.Concat(ExtImagen).ToArray(),
            "Se lee con reconocimiento de texto (OCR). Puede equivocarse: revisa la lista. ~10 s por página."),
        new("AutoCAD DXF", ExtDxf, "Textos y atributos de bloques (AutoCAD Electrical)."),
        new("AutoCAD DWG", ExtDwg, "Necesita «ODA File Converter» (gratuito) instalado; si no, guárdalo como DXF."),
        new("Listado Excel / CSV", new[] { ".xlsx", ".xlsm", ".csv", ".txt" },
            "Lista de bornas o de aparatos exportada de EPLAN, SEE, AutoCAD Electrical… " +
            "El .xls antiguo no: guárdalo como .xlsx."),
    };

    /// <summary>Todas las extensiones que se pueden abrir, sin repetir y en orden: ".pdf .dxf …".</summary>
    public static List<string> ExtensionesAdmitidas()
    {
        var vistas = new List<string>();
        foreach (var f in Formatos)
            foreach (var e in f.Extensiones)
                if (!vistas.Contains(e)) vistas.Add(e);
        return vistas;
    }

    public static string? TipoArchivo(string ruta)
    {
        var ext = Path.GetExtension(ruta).ToLowerInvariant();
        if (ExtPdf.Contains(ext)) return "pdf";
        if (ExtDxf.Contains(ext)) return "dxf";
        if (ExtDwg.Contains(ext)) return "dwg";
        if (ExtImagen.Contains(ext)) return "imagen";
        if (ExtTabla.Contains(ext)) return "tabla";
        return null;
    }

    /// <summary>Lee un esquema (PDF, DXF, DWG o imagen) y devuelve sus páginas.</summary>
    public static List<Pagina> LeerPaginas(string ruta, Action<string>? progreso = null)
    {
        switch (TipoArchivo(ruta))
        {
            case "pdf": return LectorPdf.Leer(ruta, progreso);
            case "dxf": return LectorDxf.Leer(ruta, progreso);
            case "dwg": return LectorDxf.LeerDwg(ruta, progreso);
            case "imagen": return LectorOcr.LeerImagen(ruta, progreso);
            default:
                throw new ErrorLectura("Formato no reconocido.\nExtensiones admitidas: " + string.Join(" ", ExtensionesAdmitidas()));
        }
    }

    internal static Direccion Dir(double x, double y) => Direccion.Redondeada(x, y);
}
