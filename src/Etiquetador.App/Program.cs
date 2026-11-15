using Avalonia;

namespace Etiquetador.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Sin ventana: «EtiquetadorCuadros --prueba ARCHIVO» escribe ARCHIVO.prueba.txt (para comprobar el programa)
        if (args.Length > 1 && args[0] == "--prueba")
        {
            Prueba.Ejecutar(args[1]);
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registrar(e.ExceptionObject?.ToString() ?? "error desconocido");
        TaskScheduler.UnobservedTaskException += (_, e) => { Registrar(e.Exception.ToString()); e.SetObserved(); };
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { Registrar(ex.ToString()); throw; }
    }

    /// <summary>Apunta los errores en %APPDATA%\EtiquetadorCuadros\error.log para poder ver qué pasó si la ventana se cierra.</summary>
    internal static void Registrar(string texto)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EtiquetadorCuadros");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{texto}\n\n");
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Dibujado por software: evita los cierres sin mensaje que da el dibujado con la tarjeta gráfica en algunos equipos
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } });
}
