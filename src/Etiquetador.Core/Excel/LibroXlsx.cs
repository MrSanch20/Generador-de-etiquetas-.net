using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Etiquetador.Core.Excel;

/// <summary>Estilos de celda que usan los libros de etiquetas.</summary>
public enum EstiloCelda
{
    Normal = 0,
    Negrita = 1,
    Cabecera = 2,        // negrita blanca sobre azul 1D4FC4
    Banda = 3,           // fondo gris EEF1F5
    NegritaBanda = 4,
}

public sealed class Hoja
{
    public string Titulo { get; }
    public List<double> Anchos { get; } = new();
    public List<List<object?>> Filas { get; } = new();
    public Dictionary<(int Fila, int Col), EstiloCelda> Estilos { get; } = new();
    /// <summary>Congelar la primera fila (cabecera).</summary>
    public bool CongelarCabecera { get; set; }

    public Hoja(string titulo) { Titulo = titulo; }

    public void Agregar(params object?[] celdas) => Filas.Add(new List<object?>(celdas));
    public void Estilo(int fila, int col, EstiloCelda e) => Estilos[(fila, col)] = e;
    public int NumColumnas => Filas.Count == 0 ? 0 : Filas.Max(f => f.Count);
}

/// <summary>
/// Escritor mínimo de libros .xlsx (sin dependencias). Valores: texto, números enteros o decimales y fórmulas
/// (texto que empieza por «=»). Los textos vacíos no escriben valor, como hace openpyxl.
/// </summary>
public sealed class LibroXlsx
{
    public List<Hoja> Hojas { get; } = new();

    public Hoja CrearHoja(string titulo)
    {
        var h = new Hoja(titulo);
        Hojas.Add(h);
        return h;
    }

    private static string ColLetra(int idx1)
    {
        var sb = new StringBuilder();
        while (idx1 > 0)
        {
            int r = (idx1 - 1) % 26;
            sb.Insert(0, (char)('A' + r));
            idx1 = (idx1 - 1) / 26;
        }
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;   // caracteres no válidos en XML
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public void Guardar(string ruta)
    {
        using var fs = new FileStream(ruta, FileMode.Create, FileAccess.Write);
        Guardar(fs);
    }

    public void Guardar(Stream destino)
    {
        var compartidas = new List<string>();
        var indice = new Dictionary<string, int>(StringComparer.Ordinal);
        int Compartida(string s)
        {
            if (!indice.TryGetValue(s, out int i)) { i = compartidas.Count; compartidas.Add(s); indice[s] = i; }
            return i;
        }

        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);

        void Escribir(string nombre, string contenido)
        {
            var entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
            using var w = new StreamWriter(entrada.Open(), new UTF8Encoding(false));
            w.Write(contenido);
        }

        // Hojas (primero, para ir reuniendo los textos compartidos)
        for (int n = 0; n < Hojas.Count; n++)
            Escribir($"xl/worksheets/sheet{n + 1}.xml", XmlHoja(Hojas[n], Compartida));

        Escribir("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            string.Concat(Enumerable.Range(1, Hojas.Count).Select(i =>
                $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
            "</Types>");

        Escribir("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        var wb = new StringBuilder();
        wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        wb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        wb.Append("<bookViews><workbookView/></bookViews><sheets>");
        for (int n = 0; n < Hojas.Count; n++)
            wb.Append($"<sheet name=\"{Esc(Hojas[n].Titulo)}\" sheetId=\"{n + 1}\" r:id=\"rId{n + 1}\"/>");
        wb.Append("</sheets></workbook>");
        Escribir("xl/workbook.xml", wb.ToString());

        var rels = new StringBuilder();
        rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int n = 0; n < Hojas.Count; n++)
            rels.Append($"<Relationship Id=\"rId{n + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{n + 1}.xml\"/>");
        rels.Append($"<Relationship Id=\"rId{Hojas.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        rels.Append($"<Relationship Id=\"rId{Hojas.Count + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
        rels.Append("</Relationships>");
        Escribir("xl/_rels/workbook.xml.rels", rels.ToString());

        Escribir("xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"3\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"00FFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"4\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"001D4FC4\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"00EEF1F5\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"5\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>");

        var ss = new StringBuilder();
        ss.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        ss.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{compartidas.Count}\" uniqueCount=\"{compartidas.Count}\">");
        foreach (var s in compartidas)
        {
            bool espacios = s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]) || s.Contains('\n'));
            ss.Append(espacios ? "<si><t xml:space=\"preserve\">" : "<si><t>").Append(Esc(s)).Append("</t></si>");
        }
        ss.Append("</sst>");
        Escribir("xl/sharedStrings.xml", ss.ToString());
    }

    private string XmlHoja(Hoja h, Func<string, int> compartida)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (h.CongelarCabecera)
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                      "<selection pane=\"bottomLeft\"/></sheetView></sheetViews>");
        else
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
        sb.Append("<sheetFormatPr baseColWidth=\"8\" defaultRowHeight=\"15\"/>");
        if (h.Anchos.Count > 0)
        {
            sb.Append("<cols>");
            for (int i = 0; i < h.Anchos.Count; i++)
                sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Num(h.Anchos[i])}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < h.Filas.Count; r++)
        {
            var fila = h.Filas[r];
            // las celdas con estilo (aunque estén vacías) también se escriben
            int ancho = fila.Count;
            foreach (var (k, _) in h.Estilos) if (k.Fila == r && k.Col + 1 > ancho) ancho = k.Col + 1;
            if (ancho == 0) continue;
            sb.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < ancho; c++)
            {
                object? v = c < fila.Count ? fila[c] : null;
                h.Estilos.TryGetValue((r, c), out var est);
                string rf = ColLetra(c + 1) + (r + 1);
                string s = est != EstiloCelda.Normal ? $" s=\"{(int)est}\"" : "";
                switch (v)
                {
                    case null:
                    case string { Length: 0 }:
                        if (est != EstiloCelda.Normal) sb.Append($"<c r=\"{rf}\"{s}/>");
                        break;
                    case string f when f.StartsWith('='):
                        sb.Append($"<c r=\"{rf}\"{s}><f>{Esc(f[1..])}</f><v></v></c>");
                        break;
                    case string t:
                        sb.Append($"<c r=\"{rf}\"{s} t=\"s\"><v>{compartida(t)}</v></c>");
                        break;
                    case bool b:
                        sb.Append($"<c r=\"{rf}\"{s} t=\"b\"><v>{(b ? 1 : 0)}</v></c>");
                        break;
                    case int or long or short or byte:
                        sb.Append($"<c r=\"{rf}\"{s}><v>{Convert.ToInt64(v, CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case double d:
                        sb.Append($"<c r=\"{rf}\"{s}><v>{Num(d)}</v></c>");
                        break;
                    default:
                        sb.Append($"<c r=\"{rf}\"{s} t=\"s\"><v>{compartida(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")}</v></c>");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }
}
