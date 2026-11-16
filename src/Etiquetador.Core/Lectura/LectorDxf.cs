using Etiquetador.Core.Lectura.Dxf;

namespace Etiquetador.Core.Lectura;

/// <summary>
/// Lectura de DXF de AutoCAD (texto ASCII) sin dependencias: TEXT, MTEXT y ATTRIB (también dentro de bloques),
/// y las líneas del dibujo para separar hojas. El DWG se convierte antes a DXF con «ODA File Converter».
/// El trabajo está repartido en <c>Lectura/Dxf</c>: analizador, extractor de textos, constructor de página,
/// separador de hojas y conversor de DWG.
/// </summary>
public static class LectorDxf
{
    public static List<Pagina> Leer(string ruta, Action<string>? progreso = null)
    {
        progreso?.Invoke("Abriendo el DXF…");
        DocumentoDxf dxf;
        try { dxf = AnalizadorDxf.Analizar(ruta); }
        catch (ErrorLectura) { throw; }
        catch (Exception e) { throw new ErrorLectura($"No se puede abrir el DXF: {e.Message}"); }
        return PaginasDeDxf(dxf);
    }

    /// <summary>El DWG es un formato cerrado: se convierte a DXF con ODA File Converter (gratuito).</summary>
    public static List<Pagina> LeerDwg(string ruta, Action<string>? progreso = null) =>
        ConversorDwg.Leer(ruta, r => Leer(r, null), progreso);

    private static List<Pagina> PaginasDeDxf(DocumentoDxf dxf)
    {
        var paginas = new List<Pagina>();
        // 1) Presentaciones (cada una es una hoja), si tienen texto propio. Las entidades de la presentación activa
        //    van en ENTITIES (código 67 = 1); las demás, en bloques "*Paper_Space…".
        var presentaciones = new List<List<EntidadDxf>>();
        var activa = dxf.Entidades.Where(e => e.Ent32(67) == 1).ToList();
        if (activa.Count > 0) presentaciones.Add(activa);
        foreach (var b in dxf.OrdenBloques)
            if (b.Nombre.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase))
                presentaciones.Add(b.Entidades);
        foreach (var ents in presentaciones)
        {
            var textos = ExtractorTextosDxf.Textos(ents, dxf, Transf.Identidad).ToList();
            if (textos.Count >= 5)
            {
                var pag = ConstructorPaginaDxf.Crear(paginas.Count + 1, textos);
                if (pag != null) paginas.Add(pag);
            }
        }
        // 2) Si no, el espacio modelo (separando hojas si hay varias)
        if (paginas.Count == 0)
        {
            var msp = dxf.Entidades.Where(e => e.Ent32(67) != 1).ToList();
            var textos = ExtractorTextosDxf.Textos(msp, dxf, Transf.Identidad).ToList();
            if (textos.Count == 0) throw new ErrorLectura("El DXF no tiene textos que leer.");
            foreach (var hoja in SeparadorHojasDxf.SepararHojas(textos, SeparadorHojasDxf.CajasGeometria(msp)))
            {
                var pag = ConstructorPaginaDxf.Crear(paginas.Count + 1, hoja);
                if (pag != null) paginas.Add(pag);
            }
        }
        return paginas;
    }
}
