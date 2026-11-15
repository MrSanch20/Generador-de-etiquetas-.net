namespace Etiquetador.Core.Aplicacion;

/// <summary>Lee un esquema entero (páginas -> bornas, elementos y cables). Se puede usar desde otro hilo: no toca la sesión.</summary>
public static class CargadorEsquemas
{
    /// <summary>Lee un esquema (PDF, DXF, DWG o imagen). Se puede llamar desde otro hilo.</summary>
    public static ResultadoArchivo Leer(string ruta, Action<string>? progreso = null)
    {
        try
        {
            var paginas = LectorArchivos.LeerPaginas(ruta, progreso);
            progreso?.Invoke("buscando bornas y elementos…");
            var bornas = ExtractorBornas.Extraer(paginas);
            var elementos = ExtractorElementos.Extraer(paginas);
            var cables = ExtractorCables.Extraer(paginas);
            // páginas leídas con OCR que han aportado bornas o elementos (para avisar)
            var usadas = new HashSet<object>();
            foreach (var b in bornas) usadas.Add(b.Pagina);
            foreach (var e in elementos) foreach (var n in e.Paginas) usadas.Add(n);
            var ocr = paginas.Where(p => p.Origen == "ocr" && usadas.Contains(p.Numero)).Select(p => p.Numero).ToList();
            var sinOcr = paginas.Where(p => p.SinOcr).Select(p => p.Numero).ToList();
            var nombre = IdentificadorEsquema.Buscar(paginas) ?? Sesion.NombreDe(ruta);
            return new ResultadoArchivo { Ruta = ruta, Nombre = nombre, Bornas = bornas, Elementos = elementos, Cables = cables, Ocr = ocr, SinOcr = sinOcr };
        }
        catch (ErrorLectura e)
        {
            return new ResultadoArchivo { Ruta = ruta, Nombre = Sesion.NombreDe(ruta), Error = e.Message };
        }
        catch (Exception e)
        {
            return new ResultadoArchivo { Ruta = ruta, Nombre = Sesion.NombreDe(ruta), Error = $"No se ha podido leer el archivo:\n{e.Message}" };
        }
    }
}
