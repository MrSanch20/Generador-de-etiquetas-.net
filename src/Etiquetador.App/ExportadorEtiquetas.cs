using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Etiquetador.App;

/// <summary>Guardar el Excel de la lista activa y copiar sus etiquetas al portapapeles.</summary>
public sealed class ExportadorEtiquetas
{
    private readonly Window _ventana;
    private readonly Sesion _s;
    private readonly Action<string> _estado;
    private readonly Func<string, Task> _aviso;
    private readonly string _titulo;

    public ExportadorEtiquetas(Window ventana, Sesion sesion, Action<string> estado, Func<string, Task> aviso, string titulo)
    {
        _ventana = ventana; _s = sesion; _estado = estado; _aviso = aviso; _titulo = titulo;
    }

    public async Task Guardar(Que que)
    {
        string nombreQue = Sesion.NombreQue(que);
        IStorageFolder? carpeta = null;
        try
        {
            if (_s.RutaActual != null)
                carpeta = await _ventana.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_s.RutaActual) ?? "");
        }
        catch (Exception) { }
        var archivo = await _ventana.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Guardar Excel de {nombreQue}",
            DefaultExtension = "xlsx",
            SuggestedFileName = _s.NombreExcelPropuesto(que),
            SuggestedStartLocation = carpeta,
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } },
        });
        var ruta = archivo?.TryGetLocalPath();
        if (ruta == null) return;
        try
        {
            _s.GuardarExcel(que, ruta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _aviso("No se puede guardar: ese Excel está abierto. Ciérralo y vuelve a intentarlo.");
            return;
        }
        _estado($"Excel de {nombreQue} guardado: {Path.GetFileName(ruta)} ({_s.Textos(que).Count} etiquetas)");
        if (OperatingSystem.IsWindows() && await Dialogos.Preguntar(_ventana, _titulo, $"Excel de {nombreQue} guardado.\n¿Quieres abrirlo ahora?"))
        {
            try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }

    public async Task Copiar(Que que)
    {
        var textos = _s.Textos(que);
        if (_ventana.Clipboard != null) await _ventana.Clipboard.SetTextAsync(string.Join("\n", textos));
        _estado($"Copiadas {textos.Count} etiquetas ({Sesion.NombreQue(que)}), una por línea. Pégalas en Excel o en GeniusPro.");
    }
}
