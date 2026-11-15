using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Etiquetador.Core.Configuracion;

/// <summary>
/// Ajustes que el programa recuerda entre usos (en %APPDATA%\EtiquetadorCuadros\config.json,
/// el mismo archivo y formato que la versión Python):
///   - SW-CODE por defecto de los elementos (181 si no se ha cambiado)
///   - SW-CODE de cada regleta (XALI, XSEN…), el último que se puso
///   - SW-CODE de los cables de fuerza y de maniobra, el último que se puso
/// </summary>
public sealed class Ajustes
{
    public string SwcodeElementos { get; set; } = Config.SwcodeElementosDefecto;
    public Dictionary<string, string> SwcodeRegletas { get; } = new();
    public Dictionary<string, string> SwcodeCables { get; } = new() { ["fuerza"] = "", ["maniobra"] = "" };

    /// <summary>Claves que no conocemos: se conservan al guardar.</summary>
    internal JsonObject? Extra { get; set; }
}

public static class Config
{
    public const string SwcodeElementosDefecto = "181";

    /// <summary>Carpeta base de los ajustes. Por defecto %APPDATA% (o ~/.config fuera de Windows).</summary>
    public static string? CarpetaBase { get; set; }

    private static string Ruta()
    {
        string baseDir = CarpetaBase
            ?? Environment.GetEnvironmentVariable("APPDATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "EtiquetadorCuadros", "config.json");
    }

    public static Ajustes Cargar()
    {
        var a = new Ajustes();
        try
        {
            var texto = File.ReadAllText(Ruta());
            if (JsonNode.Parse(texto) is JsonObject datos)
            {
                a.Extra = datos;
                if (datos["swcode_elementos"] is JsonValue v && v.TryGetValue<string>(out var s))
                    a.SwcodeElementos = s;
                if (datos["swcode_regletas"] is JsonObject regletas)
                    foreach (var kv in regletas)
                        if (kv.Value is JsonValue rv && rv.TryGetValue<string>(out var rs)) a.SwcodeRegletas[kv.Key] = rs;
                if (datos["swcode_cables"] is JsonObject cables)
                    foreach (var kv in cables)
                        if (kv.Value is JsonValue cv && cv.TryGetValue<string>(out var cs)) a.SwcodeCables[kv.Key] = cs;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // sin ajustes guardados (o ilegibles): se parte de los de por defecto
        }
        if (!a.SwcodeCables.ContainsKey("fuerza")) a.SwcodeCables["fuerza"] = "";
        if (!a.SwcodeCables.ContainsKey("maniobra")) a.SwcodeCables["maniobra"] = "";
        return a;
    }

    public static void Guardar(Ajustes a)
    {
        try
        {
            var raiz = a.Extra ?? new JsonObject();
            raiz["swcode_elementos"] = a.SwcodeElementos;
            var regletas = new JsonObject();
            foreach (var kv in a.SwcodeRegletas) regletas[kv.Key] = kv.Value;
            raiz["swcode_regletas"] = regletas;
            var cables = new JsonObject();
            foreach (var kv in a.SwcodeCables) cables[kv.Key] = kv.Value;
            raiz["swcode_cables"] = cables;

            var ruta = Ruta();
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            var opciones = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(ruta, raiz.ToJsonString(opciones), new System.Text.UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // si no se puede guardar, el programa sigue funcionando igual
        }
    }
}
