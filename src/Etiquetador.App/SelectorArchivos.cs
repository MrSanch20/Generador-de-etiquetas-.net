using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Etiquetador.App;

/// <summary>Diálogos de elegir esquemas y de información sobre los formatos admitidos.</summary>
public static class SelectorArchivos
{
    public static async Task<List<string>> Elegir(Window ventana, bool anadir)
    {
        var tipos = new List<FilePickerFileType>
        {
            new("Esquemas y listados") { Patterns = LectorArchivos.ExtensionesAdmitidas().Select(e => "*" + e).ToList() },
            new("PDF") { Patterns = new[] { "*.pdf" } },
            new("AutoCAD (DXF, DWG)") { Patterns = new[] { "*.dxf", "*.dwg" } },
            new("Imagen escaneada") { Patterns = LectorArchivos.ExtImagen.Select(e => "*" + e).ToList() },
            new("Listado Excel / CSV") { Patterns = new[] { "*.xlsx", "*.xlsm", "*.csv", "*.txt" } },
            FilePickerFileTypes.All,
        };
        var archivos = await ventana.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = anadir ? "Añadir esquemas" : "Elegir esquemas (puedes elegir varios)",
            AllowMultiple = true,
            FileTypeFilter = tipos,
        });
        return archivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task MostrarFormatos(Window ventana)
    {
        string texto = string.Join("\n\n", LectorArchivos.Formatos.Select(f => $"{f.Nombre}  ({string.Join("  ", f.Extensiones)})\n{f.Nota}"));
        texto += "\n\nOCR en este programa: " + (LectorOcr.Disponible
            ? "SÍ incluido."
            : "NO incluido (no lee planos escaneados ni imágenes). Usa PDF con texto, DXF o listados Excel/CSV.");
        await Dialogos.Mensaje(ventana, "Formatos admitidos", texto);
    }
}
