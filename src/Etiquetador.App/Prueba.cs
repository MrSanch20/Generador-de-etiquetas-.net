using Etiquetador.Core;

namespace Etiquetador.App;

internal static class Prueba
{
    /// <summary>Lee el archivo y escribe el resumen en ARCHIVO.prueba.txt (mismo formato que la versión Python).</summary>
    public static void Ejecutar(string ruta)
    {
        var lineas = new List<string>();
        try
        {
            var paginas = Lectores.LeerPaginas(ruta, _ => { });
            var bornas = Extraccion.ExtraerBornas(paginas);
            var elementos = Extraccion.SoloPlaca(Extraccion.ExtraerElementos(paginas));
            var cables = Extraccion.ExtraerCables(paginas).Where(c => c.Numero.Length > 0).ToList();
            lineas.Add($"cables_fuerza={cables.Count(c => c.Tipo == "Fuerza")}");
            lineas.Add($"cables_maniobra={cables.Count(c => c.Tipo == "Maniobra")}");
            lineas.Add($"paginas={paginas.Count}");
            lineas.Add("ocr=[" + string.Join(", ", paginas.Where(p => p.Origen == "ocr").Select(p => p.Numero)) + "]");
            lineas.Add($"bornas={bornas.Count}");
            lineas.Add($"elementos_placa={elementos.Count}");
            lineas.Add("primeras_bornas=" + string.Join(",", bornas.Take(10).Select(b => b.Marca)));
            lineas.Add("primeros_elementos=" + string.Join(",", elementos.Take(10).Select(e => e.Designacion)));
        }
        catch (Exception e)
        {
            lineas.Add($"ERROR: {e.Message}");
        }
        File.WriteAllText(ruta + ".prueba.txt", string.Join("\n", lineas) + "\n");
    }
}
