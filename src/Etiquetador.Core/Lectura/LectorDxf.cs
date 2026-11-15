using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Lectura;

/// <summary>
/// Lectura de DXF de AutoCAD (texto ASCII) sin dependencias: TEXT, MTEXT y ATTRIB (también dentro de bloques),
/// y las líneas del dibujo para separar hojas. El DWG se convierte antes a DXF con «ODA File Converter».
/// </summary>
public static class LectorDxf
{
    // ---------------------------------------------------------------------
    // Estructura del DXF
    // ---------------------------------------------------------------------
    private sealed class Ent
    {
        public string Tipo = "";
        public List<(int Cod, string Val)> Datos = new();
        public List<Ent> Hijos = new();      // ATTRIB de un INSERT, VERTEX de una POLYLINE

        public string? Str(int cod) { foreach (var (c, v) in Datos) if (c == cod) return v; return null; }
        public double Num(int cod, double defecto = 0)
        {
            foreach (var (c, v) in Datos)
                if (c == cod && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return defecto;
        }
        public bool Tiene(int cod) { foreach (var (c, _) in Datos) if (c == cod) return true; return false; }
        public int Ent32(int cod, int defecto = 0) => (int)Math.Round(Num(cod, defecto));
        public IEnumerable<string> Todos(int cod) => Datos.Where(d => d.Cod == cod).Select(d => d.Val);
    }

    private sealed class Bloque
    {
        public string Nombre = "";
        public double BaseX, BaseY;
        public List<Ent> Entidades = new();
    }

    private sealed class Dxf
    {
        public List<Ent> Entidades = new();
        public Dictionary<string, Bloque> Bloques = new(StringComparer.OrdinalIgnoreCase);
        public List<Bloque> OrdenBloques = new();
    }

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

    private static Dxf Analizar(string ruta)
    {
        var pares = Pares(ruta);
        var dxf = new Dxf();
        string seccion = "";
        Bloque? bloque = null;
        Ent? actual = null;           // entidad en curso (con hijos)
        Ent? ultima = null;
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

            var e = new Ent { Tipo = val };
            i++;
            while (i < pares.Count && pares[i].Item1 != 0) { e.Datos.Add(pares[i]); i++; }

            if (e.Tipo == "BLOCK")
            {
                bloque = new Bloque { Nombre = e.Str(2) ?? "", BaseX = e.Num(10), BaseY = e.Num(20) };
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

    // ---------------------------------------------------------------------
    // Textos
    // ---------------------------------------------------------------------
    private sealed record TextoDxf(string Texto, double X, double Y, double Rot, double Alto, object Estilo, double Ancho);

    private readonly record struct Transf(double Ix, double Iy, double Sx, double Sy, double RotGrados, double Bx, double By)
    {
        public static readonly Transf Identidad = new(0, 0, 1, 1, 0, 0, 0);

        public (double X, double Y) Punto(double x, double y)
        {
            double a = RotGrados * Math.PI / 180;
            double dx = (x - Bx) * Sx, dy = (y - By) * Sy;
            return (Ix + dx * Math.Cos(a) - dy * Math.Sin(a), Iy + dx * Math.Sin(a) + dy * Math.Cos(a));
        }
    }

    private static string LimpiarTexto(string s)
    {
        // %%d = °, %%p = ±, %%c = Ø, %%% = %
        s = s.Replace("%%d", "°", StringComparison.OrdinalIgnoreCase)
             .Replace("%%p", "±", StringComparison.OrdinalIgnoreCase)
             .Replace("%%c", "Ø", StringComparison.OrdinalIgnoreCase)
             .Replace("%%%", "%");
        s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
        return s;
    }

    /// <summary>Texto de un MTEXT sin los códigos de formato (\P, \f…;, {…}).</summary>
    private static string TextoMtext(Ent e)
    {
        var sb = new StringBuilder();
        foreach (var c in e.Todos(3)) sb.Append(c);
        sb.Append(e.Str(1) ?? "");
        string s = sb.ToString();
        s = s.Replace("\\\\", "\u0001").Replace("\\{", "\u0002").Replace("\\}", "\u0003");
        s = Regex.Replace(s, @"\\P", "\n");
        s = Regex.Replace(s, @"\\S([^;^]*)[\^/#]([^;]*);", "$1/$2");
        s = Regex.Replace(s, @"\\[fFcCHhTtQqWwAa][^;]*;", "");
        s = Regex.Replace(s, @"\\[LlOoKk]", "");
        s = s.Replace("\\~", " ").Replace("{", "").Replace("}", "");
        s = s.Replace("\u0001", "\\").Replace("\u0002", "{").Replace("\u0003", "}");
        return LimpiarTexto(s);
    }

    private static (double X, double Y) Dir(double grados, double largo)
    {
        double a = grados * Math.PI / 180;
        return (Math.Cos(a) * largo, Math.Sin(a) * largo);
    }

    private static IEnumerable<TextoDxf> Textos(IEnumerable<Ent> entidades, Dxf dxf, Transf t, int profundidad = 0)
    {
        foreach (var e in entidades)
        {
            List<TextoDxf> salida = new();
            try
            {
                switch (e.Tipo)
                {
                    case "INSERT":
                        foreach (var a in e.Hijos)
                            if ((a.Ent32(70) & 1) == 0)
                                salida.AddRange(Textos(new[] { a }, dxf, Transf.Identidad, profundidad + 1));
                        if (profundidad < 8 && e.Str(2) is string nombre && dxf.Bloques.TryGetValue(nombre, out var b))
                        {
                            double sx = e.Num(41, 1), sy = e.Num(42, 1);
                            var (ix, iy) = t.Punto(e.Num(10), e.Num(20));
                            var nt = new Transf(ix, iy, sx * t.Sx, sy * t.Sy, e.Num(50) + t.RotGrados, b.BaseX, b.BaseY);
                            salida.AddRange(Textos(b.Entidades, dxf, nt, profundidad + 1));
                        }
                        break;
                    case "TEXT":
                    case "ATTRIB":
                    {
                        string txt = LimpiarTexto(e.Str(1) ?? "").Trim();
                        if (txt.Length == 0) break;
                        double alto = e.Num(40) * Math.Abs(t.Sy);
                        double rot = e.Num(50) + t.RotGrados;
                        double px = e.Num(10), py = e.Num(20);
                        int halign = e.Ent32(72), valign = e.Ent32(73);
                        if ((halign != 0 || valign != 0) && e.Tiene(11)) { px = e.Num(11); py = e.Num(21); }
                        var (wx, wy) = t.Punto(px, py);
                        double ancho = txt.Length * alto * 0.75;
                        if (halign == 1 || halign == 4) { var d = Dir(rot, ancho / 2); wx -= d.X; wy -= d.Y; }
                        else if (halign == 2) { var d = Dir(rot, ancho); wx -= d.X; wy -= d.Y; }
                        salida.Add(new TextoDxf(txt, wx, wy, rot, alto,
                            (Math.Round(alto, 2, MidpointRounding.ToEven), e.Str(8) ?? "0", e.Ent32(62, 256)), ancho));
                        break;
                    }
                    case "MTEXT":
                    {
                        string txt = TextoMtext(e).Trim();
                        if (txt.Length == 0) break;
                        double alto = e.Num(40) * Math.Abs(t.Sy);
                        double rot = e.Tiene(11) ? Math.Atan2(e.Num(21), e.Num(11)) * 180 / Math.PI : e.Num(50);
                        rot += t.RotGrados;
                        var (px, py) = t.Punto(e.Num(10), e.Num(20));
                        var lineas = txt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                        double ancho = lineas.Max(l => l.Length) * alto * 0.75;
                        int att = e.Tiene(71) ? e.Ent32(71) : 1;
                        int fila = (att - 1) / 3, col = (att - 1) % 3;
                        int nLineas = lineas.Count;
                        var d1 = Dir(rot, ancho * col / 2);
                        px -= d1.X; py -= d1.Y;
                        var d2 = Dir(rot + 90, alto * nLineas * (1 - fila / 2.0) - alto);
                        px += d2.X; py += d2.Y;
                        for (int i = 0; i < lineas.Count; i++)
                        {
                            if (lineas[i].Trim().Length == 0) continue;
                            var d3 = Dir(rot + 90, i * alto * 1.6);
                            salida.Add(new TextoDxf(lineas[i].Trim(), px - d3.X, py - d3.Y, rot, alto,
                                (Math.Round(alto, 2, MidpointRounding.ToEven), e.Str(8) ?? "0", e.Ent32(62, 256)),
                                lineas[i].Length * alto * 0.75));
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                continue;      // una entidad rara no debe parar la lectura
            }
            foreach (var s in salida) yield return s;
        }
    }

    // ---------------------------------------------------------------------
    // Página
    // ---------------------------------------------------------------------
    private static Pagina? PaginaDxf(int numero, List<TextoDxf> textos, string origen = "dxf")
    {
        if (textos.Count == 0) return null;
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var t in textos)
        {
            xs.Add(t.X); xs.Add(t.X + t.Ancho);
            ys.Add(t.Y); ys.Add(t.Y + t.Alto);
        }
        double xmin = xs.Min(), xmax = xs.Max(), ymin = ys.Min(), ymax = ys.Max();
        double altoPag = Math.Max(ymax - ymin, 1e-6) * 1.02;
        double esc = LectorArchivos.AltoRef / altoPag;
        var pag = new Pagina(numero, (xmax - xmin) * esc, LectorArchivos.AltoRef, origen);
        foreach (var t in textos)
        {
            double r = ((t.Rot % 360) + 360) % 360;
            Direccion d; double x0, x1, yb0, yb1;
            if (Math.Abs(r - 90) < 5)
            {
                d = Direccion.Vertical;
                // texto girado 90°: crece hacia arriba desde (x, y); el cuerpo queda a la izquierda de x
                x0 = t.X - t.Alto; x1 = t.X;
                yb0 = t.Y; yb1 = t.Y + t.Ancho;
            }
            else
            {
                d = (r < 5 || r > 355) ? Direccion.Horizontal
                    : Direccion.Redondeada(Math.Cos(r * Math.PI / 180), -Math.Sin(r * Math.PI / 180));
                x0 = t.X; x1 = t.X + t.Ancho;
                yb0 = t.Y; yb1 = t.Y + t.Alto;
            }
            double X0 = (x0 - xmin) * esc, X1 = (x1 - xmin) * esc;
            double Y0 = (ymax - yb1) * esc, Y1 = (ymax - yb0) * esc;
            var tp = new TextoPos(t.Texto, X0, Y0, X1, Y1, d, t.Alto * esc, t.Estilo);
            pag.Trozos.Add(tp);
            pag.Lineas.Add(tp);
        }
        return pag;
    }

    /// <summary>Rectángulos (xmin, xmax, ymin, ymax) de líneas y polilíneas del dibujo.</summary>
    private static List<(double, double, double, double)> CajasGeometria(IEnumerable<Ent> entidades)
    {
        var res = new List<(double, double, double, double)>();
        foreach (var e in entidades)
        {
            try
            {
                var pts = new List<(double X, double Y)>();
                switch (e.Tipo)
                {
                    case "LINE":
                        pts.Add((e.Num(10), e.Num(20))); pts.Add((e.Num(11), e.Num(21)));
                        break;
                    case "LWPOLYLINE":
                    {
                        double? x = null;
                        foreach (var (c, v) in e.Datos)
                        {
                            if (c == 10) x = double.Parse(v.Trim(), CultureInfo.InvariantCulture);
                            else if (c == 20 && x != null) { pts.Add((x.Value, double.Parse(v.Trim(), CultureInfo.InvariantCulture))); x = null; }
                        }
                        break;
                    }
                    case "POLYLINE":
                        foreach (var v in e.Hijos) pts.Add((v.Num(10), v.Num(20)));
                        break;
                    default: continue;
                }
                if (pts.Count > 0)
                    res.Add((pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y)));
            }
            catch (Exception) { }
        }
        return res;
    }

    /// <summary>Une intervalos (a, b) que se solapan o tocan; devuelve los grupos resultantes.</summary>
    private static List<double[]> Grupos(IEnumerable<(double, double)> intervalos)
    {
        var grupos = new List<double[]>();
        foreach (var (a, b) in intervalos.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            if (grupos.Count > 0 && a <= grupos[^1][1]) grupos[^1][1] = Math.Max(grupos[^1][1], b);
            else grupos.Add(new[] { a, b });
        }
        return grupos;
    }

    /// <summary>
    /// Si el espacio modelo tiene varias hojas (una al lado de otra o en cuadrícula), las separa por las franjas
    /// vacías: ni textos ni líneas (el marco de cada hoja la mantiene unida).
    /// </summary>
    private static List<List<TextoDxf>> SepararHojas(List<TextoDxf> textos, List<(double, double, double, double)> cajas)
    {
        static (double, double, double, double) CajaTexto(TextoDxf t) =>
            Math.Abs(((t.Rot % 360) + 360) % 360 - 90) < 5 ? (t.X - t.Alto, t.X, t.Y, t.Y + t.Ancho) : (t.X, t.X + t.Ancho, t.Y, t.Y + t.Alto);

        var elementos = textos.Select(t => (K: 't', C: CajaTexto(t), T: (TextoDxf?)t)).ToList();
        elementos.AddRange(cajas.Select(c => (K: 'g', C: c, T: (TextoDxf?)null)));
        const double margen = 1e-6;
        var hojas = new List<(double X, double Y, List<TextoDxf> Textos)>();
        foreach (var gx in Grupos(elementos.Select(e => (e.C.Item1 - margen, e.C.Item2 + margen))))
        {
            var col = elementos.Where(e => gx[0] <= (e.C.Item1 + e.C.Item2) / 2 && (e.C.Item1 + e.C.Item2) / 2 <= gx[1]).ToList();
            foreach (var gy in Grupos(col.Select(e => (e.C.Item3 - margen, e.C.Item4 + margen))).OrderBy(g => -g[1]))
            {
                var hoja = col.Where(e => e.K == 't' && gy[0] <= (e.C.Item3 + e.C.Item4) / 2 && (e.C.Item3 + e.C.Item4) / 2 <= gy[1])
                              .Select(e => e.T!).ToList();
                if (hoja.Count >= 3) hojas.Add((gx[0], -gy[1], hoja));
            }
        }
        // de arriba abajo y de izquierda a derecha
        var ordenadas = hojas.OrderBy(h => (int)Math.Round(h.Y, MidpointRounding.ToEven)).ThenBy(h => h.X).Select(h => h.Textos).ToList();
        return ordenadas.Count > 0 ? ordenadas : new List<List<TextoDxf>> { textos };
    }

    // ---------------------------------------------------------------------
    // API
    // ---------------------------------------------------------------------
    public static List<Pagina> Leer(string ruta, Action<string>? progreso = null)
    {
        progreso?.Invoke("Abriendo el DXF…");
        Dxf dxf;
        try { dxf = Analizar(ruta); }
        catch (ErrorLectura) { throw; }
        catch (Exception e) { throw new ErrorLectura($"No se puede abrir el DXF: {e.Message}"); }
        return PaginasDeDxf(dxf);
    }

    private static List<Pagina> PaginasDeDxf(Dxf dxf)
    {
        var paginas = new List<Pagina>();
        // 1) Presentaciones (cada una es una hoja), si tienen texto propio. Las entidades de la presentación activa
        //    van en ENTITIES (código 67 = 1); las demás, en bloques "*Paper_Space…".
        var presentaciones = new List<List<Ent>>();
        var activa = dxf.Entidades.Where(e => e.Ent32(67) == 1).ToList();
        if (activa.Count > 0) presentaciones.Add(activa);
        foreach (var b in dxf.OrdenBloques)
            if (b.Nombre.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase))
                presentaciones.Add(b.Entidades);
        foreach (var ents in presentaciones)
        {
            var textos = Textos(ents, dxf, Transf.Identidad).ToList();
            if (textos.Count >= 5)
            {
                var pag = PaginaDxf(paginas.Count + 1, textos);
                if (pag != null) paginas.Add(pag);
            }
        }
        // 2) Si no, el espacio modelo (separando hojas si hay varias)
        if (paginas.Count == 0)
        {
            var msp = dxf.Entidades.Where(e => e.Ent32(67) != 1).ToList();
            var textos = Textos(msp, dxf, Transf.Identidad).ToList();
            if (textos.Count == 0) throw new ErrorLectura("El DXF no tiene textos que leer.");
            foreach (var hoja in SepararHojas(textos, CajasGeometria(msp)))
            {
                var pag = PaginaDxf(paginas.Count + 1, hoja);
                if (pag != null) paginas.Add(pag);
            }
        }
        return paginas;
    }

    // ---------------------------------------------------------------------
    // DWG (vía ODA File Converter)
    // ---------------------------------------------------------------------
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

    /// <summary>El DWG es un formato cerrado: se convierte a DXF con ODA File Converter (gratuito).</summary>
    public static List<Pagina> LeerDwg(string ruta, Action<string>? progreso = null)
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
            return Leer(dxfs[0], null);
        }
        catch (ErrorLectura) { throw; }
        catch (Exception e) { throw new ErrorLectura($"No se ha podido convertir el DWG: {e.Message}"); }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }
}
