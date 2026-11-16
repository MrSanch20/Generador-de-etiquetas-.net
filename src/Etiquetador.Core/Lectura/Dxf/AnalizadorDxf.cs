using System.Globalization;
using System.Text;

namespace Etiquetador.Core.Lectura.Dxf;

/// <summary>Lee el DXF de texto (pares código/valor) y lo convierte en un <see cref="DocumentoDxf"/>.</summary>
internal static class AnalizadorDxf
{
    private static List<(int, string)> Pares(string ruta)
    {
        var bytes = File.ReadAllBytes(ruta);
        if (bytes.Length >= 22 && Encoding.ASCII.GetString(bytes, 0, 22).StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
            throw new ErrorLectura("Este DXF es binario y no se puede leer.\nEn AutoCAD, «Guardar como» DXF de texto (ASCII).");
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { texto = Encoding.Latin1.GetString(bytes); }
        if (texto.Length > 0 && texto[0] == '\uFEFF') texto = texto[1..];
        var lineas = texto.Split('\n');
        var res = new List<(int, string)>(lineas.Length / 2);
        for (int i = 0; i + 1 < lineas.Length; i += 2)
        {
            if (!int.TryParse(lineas[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cod))
                throw new ErrorLectura("No se puede abrir el DXF: formato no válido.");
            res.Add((cod, lineas[i + 1].TrimEnd('\r')));
        }
        return res;
    }

    internal static DocumentoDxf Analizar(string ruta)
    {
        var pares = Pares(ruta);
        var dxf = new DocumentoDxf();
        string seccion = "";
        BloqueDxf? bloque = null;
        EntidadDxf? actual = null;           // entidad en curso (con hijos)
        EntidadDxf? ultima = null;
        int i = 0;
        while (i < pares.Count)
        {
            var (cod, val) = pares[i];
            if (cod != 0) { i++; continue; }
            // inicio de una entidad / marca
            if (val == "SECTION")
            {
                seccion = i + 1 < pares.Count && pares[i + 1].Item1 == 2 ? pares[i + 1].Item2.Trim() : "";
                i += 2; continue;
            }
            if (val == "ENDSEC") { seccion = ""; bloque = null; actual = null; i++; continue; }
            if (val == "EOF") break;
            if (seccion != "ENTITIES" && seccion != "BLOCKS") { i++; continue; }

            var e = new EntidadDxf { Tipo = val };
            i++;
            while (i < pares.Count && pares[i].Item1 != 0) { e.Datos.Add(pares[i]); i++; }

            if (e.Tipo == "BLOCK")
            {
                bloque = new BloqueDxf { Nombre = e.Str(2) ?? "", BaseX = e.Num(10), BaseY = e.Num(20) };
                dxf.Bloques[bloque.Nombre] = bloque;
                dxf.OrdenBloques.Add(bloque);
                actual = null;
                continue;
            }
            if (e.Tipo == "ENDBLK") { bloque = null; actual = null; continue; }
            if (e.Tipo == "SEQEND") { actual = null; continue; }
            if ((e.Tipo == "ATTRIB" || e.Tipo == "VERTEX") && actual != null)
            {
                actual.Hijos.Add(e);
                continue;
            }
            var lista = bloque != null ? bloque.Entidades : dxf.Entidades;
            lista.Add(e);
            ultima = e;
            actual = e.Tipo == "INSERT" || e.Tipo == "POLYLINE" ? e : null;
        }
        _ = ultima;
        return dxf;
    }
}
