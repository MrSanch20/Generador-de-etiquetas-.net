using System.Diagnostics;

namespace Etiquetador.Core.Lectura.Dxf;

/// <summary>El DWG es un formato cerrado: se convierte a DXF con «ODA File Converter» (gratuito) y se lee ese DXF.</summary>
internal static class ConversorDwg
{
    private static string? BuscarOda()
    {
        var candidatos = new List<string>();
        foreach (var baseDir in new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files",
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
        })
        {
            try
            {
                var oda = Path.Combine(baseDir, "ODA");
                if (Directory.Exists(oda))
                    foreach (var d in Directory.GetDirectories(oda))
                    {
                        var exe = Path.Combine(d, "ODAFileConverter.exe");
                        if (File.Exists(exe)) candidatos.Add(exe);
                    }
            }
            catch (Exception) { }
        }
        candidatos.Sort(StringComparer.Ordinal);
        return candidatos.Count > 0 ? candidatos[^1] : null;
    }

        public static List<Pagina> Leer(string ruta, Func<string, List<Pagina>> leerDxf, Action<string>? progreso = null)
    {
        var exe = BuscarOda();
        if (exe == null)
            throw new ErrorLectura("Para leer DWG hace falta «ODA File Converter» (gratuito):\n" +
                                   "https://www.opendesign.com/guestfiles/oda_file_converter\n\n" +
                                   "Otra opción: en AutoCAD, «Guardar como» DXF y abrir el DXF.");
        progreso?.Invoke("Convirtiendo el DWG a DXF…");
        string tmp = Path.Combine(Path.GetTempPath(), "etiquetador_" + Guid.NewGuid().ToString("N"));
        string entrada = Path.Combine(tmp, "in"), salida = Path.Combine(tmp, "out");
        try
        {
            Directory.CreateDirectory(entrada); Directory.CreateDirectory(salida);
            File.Copy(ruta, Path.Combine(entrada, Path.GetFileName(ruta)));
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in new[] { entrada, salida, "ACAD2018", "DXF", "0", "1", Path.GetFileName(ruta) }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new ErrorLectura("No se ha podido convertir el DWG.");
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (Exception) { } throw new ErrorLectura("No se ha podido convertir el DWG: tarda demasiado."); }
            var dxfs = Directory.GetFiles(salida, "*.dxf");
            if (dxfs.Length == 0) throw new ErrorLectura("No se ha podido convertir el DWG.");
            return leerDxf(dxfs[0]);
        }
        catch (ErrorLectura) { throw; }
        catch (Exception e) { throw new ErrorLectura($"No se ha podido convertir el DWG: {e.Message}"); }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }
}
