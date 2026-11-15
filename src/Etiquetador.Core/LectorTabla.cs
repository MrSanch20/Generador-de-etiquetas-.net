using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Etiquetador.Core;

/// <summary>Tablas (Excel / CSV): listados exportados del programa de esquemas. Lector de .xlsx propio, sin dependencias.</summary>
public static class LectorTabla
{
    /// <summary>Devuelve las filas (listas de textos) de la primera hoja con datos.</summary>
    public static List<List<string>> Leer(string ruta)
    {
        var ext = Path.GetExtension(ruta).ToLowerInvariant();
        List<List<string>> filas;
        if (ext == ".csv" || ext == ".txt") filas = LeerCsv(ruta);
        else if (ext == ".xls") throw new ErrorLectura("El formato .xls antiguo no se puede leer.\nÁbrelo en Excel y guárdalo como .xlsx.");
        else filas = LeerXlsx(ruta);

        // Se conservan las filas vacías intermedias para que «fila N» coincida con el archivo
        while (filas.Count > 0 && !filas[^1].Any(c => c.Length > 0)) filas.RemoveAt(filas.Count - 1);
        if (!filas.Any(f => f.Any(c => c.Length > 0))) throw new ErrorLectura("La tabla está vacía.");
        int ancho = filas.Max(f => f.Count);
        foreach (var f in filas) while (f.Count < ancho) f.Add("");
        return filas;
    }

    // ---------------------------------------------------------------------
    // CSV
    // ---------------------------------------------------------------------
    private static List<List<string>> LeerCsv(string ruta)
    {
        var bytes = File.ReadAllBytes(ruta);
        string? datos = null;
        // utf-8-sig, cp1252, latin-1
        try { datos = new UTF8Encoding(false, true).GetString(bytes); if (datos.Length > 0 && datos[0] == '﻿') datos = datos[1..]; }
        catch (DecoderFallbackException)
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); datos = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
            catch (Exception) { datos = Encoding.Latin1.GetString(bytes); }
        }
        if (datos == null) throw new ErrorLectura("No se puede leer el CSV (codificación desconocida).");

        char delim = ElegirSeparador(datos);
        var filas = new List<List<string>>();
        var fila = new List<string>();
        var celda = new StringBuilder();
        bool comillas = false;
        for (int i = 0; i < datos.Length; i++)
        {
            char c = datos[i];
            if (comillas)
            {
                if (c == '"')
                {
                    if (i + 1 < datos.Length && datos[i + 1] == '"') { celda.Append('"'); i++; }
                    else comillas = false;
                }
                else celda.Append(c);
            }
            else if (c == '"' && celda.Length == 0) comillas = true;
            else if (c == delim) { fila.Add(celda.ToString().Trim()); celda.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < datos.Length && datos[i + 1] == '\n') i++;
                fila.Add(celda.ToString().Trim()); celda.Clear();
                filas.Add(fila); fila = new List<string>();
            }
            else celda.Append(c);
        }
        if (celda.Length > 0 || fila.Count > 0) { fila.Add(celda.ToString().Trim()); filas.Add(fila); }
        // Python: una línea vacía produce una fila vacía ([]), igual que aquí con [""]
        return filas.Select(f => f.Count == 1 && f[0].Length == 0 ? new List<string>() : f).ToList();
    }

    /// <summary>Separador más probable entre ; , tab y |: el que más se repite igual en las primeras líneas.</summary>
    private static char ElegirSeparador(string datos)
    {
        var muestra = datos.Length > 5000 ? datos[..5000] : datos;
        var lineas = muestra.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(20).ToList();
        char mejor = '\0'; int mejorPuntos = 0;
        foreach (var d in new[] { ';', ',', '\t', '|' })
        {
            var cuentas = lineas.Select(l => l.Count(c => c == d)).ToList();
            if (cuentas.Count == 0 || cuentas.All(c => c == 0)) continue;
            int moda = cuentas.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            if (moda == 0) continue;
            int coinciden = cuentas.Count(c => c == moda);
            int puntos = coinciden * 100 + moda;
            if (puntos > mejorPuntos) { mejorPuntos = puntos; mejor = d; }
        }
        if (mejor != '\0') return mejor;
        return datos.Count(c => c == ';') > datos.Count(c => c == ',') ? ';' : ',';
    }

    // ---------------------------------------------------------------------
    // XLSX
    // ---------------------------------------------------------------------
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace NsPkg = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static XDocument? Documento(ZipArchive zip, string nombre)
    {
        var e = zip.GetEntry(nombre);
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    private static List<List<string>> LeerXlsx(string ruta)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(ruta); }
        catch (Exception e) { throw new ErrorLectura($"No se puede abrir el Excel: {e.Message}"); }
        using (zip)
        {
            try
            {
                var compartidas = new List<string>();
                var ss = Documento(zip, "xl/sharedStrings.xml");
                if (ss?.Root != null)
                    foreach (var si in ss.Root.Elements(Ns + "si"))
                        compartidas.Add(string.Concat(si.Descendants(Ns + "t").Where(t => t.Parent?.Name != Ns + "rPh").Select(t => t.Value)));

                var wb = Documento(zip, "xl/workbook.xml") ?? throw new ErrorLectura("No se puede abrir el Excel: falta el libro.");
                var rels = Documento(zip, "xl/_rels/workbook.xml.rels");
                var destinos = rels?.Root?.Elements(NsPkg + "Relationship")
                    .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!) ?? new Dictionary<string, string>();

                foreach (var hoja in wb.Root!.Element(Ns + "sheets")!.Elements(Ns + "sheet"))
                {
                    var id = (string?)hoja.Attribute(NsR + "id");
                    if (id == null || !destinos.TryGetValue(id, out var destino)) continue;
                    destino = destino.TrimStart('/');
                    string path = destino.StartsWith("xl/", StringComparison.Ordinal) ? destino : "xl/" + destino;
                    var xml = Documento(zip, path);
                    if (xml?.Root == null) continue;
                    var filas = FilasDeHoja(xml, compartidas);
                    if (filas.Any(f => f.Any(c => c.Length > 0))) return filas;
                }
                return new List<List<string>>();
            }
            catch (ErrorLectura) { throw; }
            catch (Exception e) { throw new ErrorLectura($"No se puede abrir el Excel: {e.Message}"); }
        }
    }

    private static int ColIndice(string referencia)
    {
        int n = 0;
        foreach (var c in referencia)
        {
            if (c < 'A' || c > 'Z') break;
            n = n * 26 + (c - 'A' + 1);
        }
        return n - 1;
    }

    private static List<List<string>> FilasDeHoja(XDocument xml, List<string> compartidas)
    {
        var filas = new List<List<string>>();
        var datos = xml.Root!.Element(Ns + "sheetData");
        if (datos == null) return filas;
        int numFila = 0;
        foreach (var row in datos.Elements(Ns + "row"))
        {
            int r = (int?)row.Attribute("r") ?? numFila + 1;
            while (numFila < r - 1) { filas.Add(new List<string>()); numFila++; }     // filas vacías intermedias
            var fila = new List<string>();
            int col = 0;
            foreach (var c in row.Elements(Ns + "c"))
            {
                var rf = (string?)c.Attribute("r");
                if (rf != null) col = ColIndice(rf);
                while (fila.Count < col) fila.Add("");
                fila.Add(ValorCelda(c, compartidas));
                col++;
            }
            filas.Add(fila);
            numFila = r;
        }
        return filas;
    }

    private static string ValorCelda(XElement c, List<string> compartidas)
    {
        var tipo = (string?)c.Attribute("t");
        if (tipo == "inlineStr")
            return string.Concat(c.Descendants(Ns + "t").Select(t => t.Value)).Trim();
        var v = c.Element(Ns + "v")?.Value;
        if (v == null) return "";
        switch (tipo)
        {
            case "s":
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < compartidas.Count
                    ? compartidas[i].Trim() : "";
            case "b": return v == "1" ? "True" : "False";
            case "str":
            case "e":
                return v.Trim();
            default:
                return FormatoNumero(v);
        }
    }

    /// <summary>Número como lo da Python con str(): "5" si es entero, "5.5" si no.</summary>
    private static string FormatoNumero(string v)
    {
        v = v.Trim();
        if (Regex.IsMatch(v, @"\A-?\d+\z")) return v;
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15 && !v.Contains('E') && !v.Contains('e')) return v;   // "5.0" se queda como viene
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
        return v;
    }
}
