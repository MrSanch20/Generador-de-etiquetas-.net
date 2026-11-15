using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core;

/// <summary>Fila de etiquetas que se puede repetir por esquema.</summary>
public interface IFila
{
    string Esquema { get; set; }
    string Copia { get; set; }
    string SwCode { get; set; }
    IFila Duplicar();
}

/// <summary>Resultado de analizar una tabla (Excel/CSV).</summary>
public sealed record AnalisisTabla(int? Cabecera, Dictionary<string, int?> Columnas, string Modo);

/// <summary>
/// Extracción de etiquetas a partir de las páginas leídas (<see cref="Lectores"/>) o de una tabla (Excel/CSV).
///   - Bornas: páginas "Plano de bornes =...-XALI" -> marcas de cada borna.
///   - Elementos: designaciones del esquema (Q100, KM100, K20…) montados en la placa.
///   - Cables: hilos con su número y color, seguidos hasta sus extremos.
/// </summary>
public static class Extraccion
{
    public const double MaxDistDescripcion = 50;   // distancia máx. borna <-> texto descriptivo
    public const double Linea = 15;                // alto (pt) que se considera "la misma línea" al ordenar
    public const double GapFila = 30;              // separación vertical (pt) mínima entre dos filas de carriles de la placa
    public const double Pie = 0.93;                // por debajo de esta altura (fracción) está el cajetín

    // Título de las páginas de bornas, en varios idiomas
    private static readonly Regex TituloRe = Rx.Crear(@"(?:\bbornes|\bterminals?(?: \w+)?|klemmen\w*)\s*=\s*\S+?-(\S+?)\s*$", RegexOptions.IgnoreCase);

    // Palabras del título de las páginas de armario (placa / puerta)
    private static readonly (string Zona, string[] Palabras)[] Zonas =
    {
        ("Placa", new[] { "PLACA", "PLAQUE", "MOUNTING PLATE", "MONTAGEPLATTE" }),
        ("Puerta", new[] { "PUERTA", "PORTE", "DOOR", "TÜR", "TUER" }),
    };

    // Designación de elemento: letras + número (KM100, Q20, H20.1, ATS1.0, S0.1…)
    private static readonly Regex EquipoRe = Rx.Crear(@"[A-Z]+\d+(?:\.\d+)?");
    private static readonly Regex NoEquipoRe = Rx.Crear(@"^(L\d|N|PE)$");
    private static readonly Regex SoloLetrasRe = Rx.Crear(@"[A-Z]{2,}");

    public static readonly (string Pref, string Tipo)[] TiposLista =
    {
        ("ATS", "Arrancador suave"), ("KM", "Contactor"), ("FC", "Final de carrera"),
        ("K", "Relé"), ("Q", "Interruptor / guardamotor"), ("F", "Protección"), ("H", "Piloto"),
        ("S", "Pulsador / selector"), ("M", "Motor"), ("U", "Fuente / equipo electrónico"),
        ("X", "Regleta de bornas"), ("R", "Resistencia"),
    };
    private static readonly Dictionary<string, string> Tipos = TiposLista.ToDictionary(p => p.Pref, p => p.Tipo);
    private static readonly (string Pref, string Tipo)[] TiposPorLongitud =
        TiposLista.OrderByDescending(p => p.Pref.Length).ToArray();   // estable: el más largo primero

    public static readonly HashSet<string> DentroArmario = new() { "Contactor", "Relé", "Interruptor / guardamotor", "Protección", "Arrancador suave" };
    private static readonly HashSet<string> TiposPlaca = new(DentroArmario) { "Fuente / equipo electrónico", "Resistencia" };
    private static readonly HashSet<string> FueraOPuerta = new() { "Motor", "Piloto", "Pulsador / selector", "Final de carrera" };

    private static int R(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

    /// <summary>Tipo según el prefijo de letras (el más largo que coincida).</summary>
    public static string TipoEquipo(string designacion)
    {
        var m = Regex.Match(designacion.ToUpperInvariant(), @"\A[A-Z]+");
        string letras = m.Success ? m.Value : "";
        foreach (var (pref, tipo) in TiposPorLongitud)
            if (letras.StartsWith(pref, StringComparison.Ordinal)) return tipo;
        return "Otros";
    }

    /// <summary>Clave de orden natural: (letras en mayúsculas, números, texto).</summary>
    public static (string Letras, List<double> Numeros, string Texto) OrdenNatural(string d)
    {
        var m = Regex.Match(d, @"\A[A-Za-z]+");
        var nums = Regex.Matches(d, @"\d+(?:\.\d+)?").Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
        return (m.Success ? m.Value.ToUpperInvariant() : "", nums, d);
    }

    private static int CmpOrdenNatural(string a, string b)
    {
        var x = OrdenNatural(a); var y = OrdenNatural(b);
        int c = string.CompareOrdinal(x.Letras, y.Letras);
        if (c != 0) return c;
        int n = Math.Min(x.Numeros.Count, y.Numeros.Count);
        for (int i = 0; i < n; i++)
        {
            c = x.Numeros[i].CompareTo(y.Numeros[i]);
            if (c != 0) return c;
        }
        c = x.Numeros.Count.CompareTo(y.Numeros.Count);
        if (c != 0) return c;
        return string.CompareOrdinal(x.Texto, y.Texto);
    }

    /// <summary>
    /// Solo los elementos cableados en la placa (KM, Q, K, F…). Los de la puerta y los de fuera del
    /// armario (motores, finales de carrera…) no llevan etiqueta. Si no hay plano de placa, se quitan los tipos
    /// que van en la puerta o fuera.
    /// </summary>
    public static List<Elemento> SoloPlaca(IEnumerable<Elemento> equipos)
    {
        var lista = equipos.Where(e => e.Tipo != Tipos["X"]).ToList();   // sin regletas de bornas
        if (lista.Any(e => e.Ubicacion == "Placa"))
            return lista.Where(e => e.Ubicacion == "Placa").ToList();
        return lista.Where(e => !FueraOPuerta.Contains(e.Tipo)).ToList();
    }

    private static string? RegletaDe(Pagina pagina)
    {
        foreach (var t in pagina.Lineas)
        {
            if (t.Dir == Direccion.Horizontal)
            {
                var m = TituloRe.Match(t.Texto);
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    // --------------------------------------------------------------------------
    // Bornas
    // --------------------------------------------------------------------------
    public static List<Borna> ExtraerBornas(IEnumerable<Pagina> paginas)
    {
        var filas = new List<Borna>();
        var contador = new Dictionary<string, int>();
        foreach (var pag in paginas)
        {
            var regleta = RegletaDe(pag);
            if (string.IsNullOrEmpty(regleta)) continue;     // no es un plano de bornes
            var verticales = pag.Lineas.Where(t => t.Dir == Direccion.Vertical && t.Y1 < pag.Alto * Pie).ToList();
            if (verticales.Count == 0) continue;
            // Las marcas son la fila de textos verticales más alta (justo encima de la regleta dibujada);
            // el resto, más abajo, son descripciones.
            double tam = verticales.Select(t => t.Tam).OrderBy(x => x).ToList()[verticales.Count / 2];
            double tol; int yMarcas;
            if (pag.Origen == "pdf")
            {
                tol = 2;
                yMarcas = verticales.Min(t => R(t.Y1));
            }
            else
            {
                // escaneado/DXF: las marcas no están perfectamente alineadas; se toma la fila más alta que tenga bastantes textos
                tol = Math.Max(2, tam * 1.2);
                var vecinos = new MapaOrdenado<int, int>();
                foreach (var t in verticales)
                    vecinos[R(t.Y1)] = verticales.Count(o => Math.Abs(o.Y1 - t.Y1) <= tol);
                int mejor = vecinos.Pares().Max(p => p.Value);
                double umbral = Math.Max(2, mejor * 0.5);
                yMarcas = vecinos.Pares().Where(p => p.Value >= umbral).Min(p => p.Key);
            }
            var marcas = verticales.Where(t => Math.Abs(R(t.Y1) - yMarcas) <= tol).OrderBy(t => t.X0).ToList();
            var desc = verticales.Where(t => R(t.Y1) > yMarcas + 50).Select(t => ((t.X0 + t.X1) / 2, t.Texto)).ToList();
            foreach (var t in marcas)
            {
                double cx = t.X0 + 5;
                var cerca = desc.Where(d => Math.Abs(d.Item1 - cx) <= MaxDistDescripcion).ToList();
                string texto = cerca.Count > 0 ? cerca.MinBy(d => Math.Abs(d.Item1 - cx)).Texto : "";
                contador[regleta] = contador.GetValueOrDefault(regleta) + 1;
                filas.Add(new Borna
                {
                    Regleta = regleta, Pos = contador[regleta], Marca = t.Texto,
                    Descripcion = texto, Pagina = pag.Numero,
                });
            }
        }
        return filas;
    }

    // --------------------------------------------------------------------------
    // Elementos
    // --------------------------------------------------------------------------
    private sealed record Candidato(string Texto, double Tam, object? Estilo, bool Horiz, int Pagina, bool EsOcr);

    private static int CmpTupla3(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        for (int i = 0; i < a.Count; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    /// <summary>
    /// Designaciones de elementos (KM100, Q100…) de las páginas de esquema. En el esquema las designaciones
    /// tienen todas el mismo estilo (mismo color y tamaño, texto horizontal). Se toma el estilo con más
    /// designaciones distintas y se quedan solo los textos que lo cumplen.
    /// </summary>
    public static List<Elemento> ExtraerElementos(IEnumerable<Pagina> paginasEntrada)
    {
        var paginas = paginasEntrada.ToList();
        var todos = new List<(string Texto, bool Horiz, bool Ocr)>();            // designaciones válidas de cualquier estilo
        var primera = new Dictionary<string, (int, int, double)>();              // designación -> (página, línea, x) de su primera aparición en el esquema
        var placaPos = new Dictionary<string, (double Tam, int Pagina, double Y, double X)>();   // rótulo grande en el plano de la placa
        var primeraArmario = new Dictionary<string, (int, int, double)>();
        var candidatos = new List<Candidato>();
        var zonas = new Dictionary<string, string> { ["Placa"] = "", ["Puerta"] = "" };

        static int CmpPos((int, int, double) a, (int, int, double) b)
        {
            int c = a.Item1.CompareTo(b.Item1);
            if (c != 0) return c;
            c = a.Item2.CompareTo(b.Item2);
            if (c != 0) return c;
            return a.Item3.CompareTo(b.Item3);
        }

        foreach (var pag in paginas)
        {
            string pie = pag.TextoZona(pag.Alto * Pie);
            string cuerpo = pag.TextoZona(0, pag.Alto * Pie);
            bool esArmario = false, esPlaca = false;
            foreach (var (zona, palabras) in Zonas)
            {
                // sin \b: el OCR a veces quita espacios ("PLACA2016VISION")
                if (palabras.Any(p => Regex.IsMatch(pie, @"(?<![A-Z])" + p + @"(?![A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                {
                    zonas[zona] += " " + cuerpo;
                    esArmario = true;
                    esPlaca = esPlaca || zona == "Placa";
                }
            }
            if (RegletaDe(pag) != null) continue;   // los planos de bornes no tienen elementos
            if (esArmario && pag.Origen == "ocr")
            {
                // En un plano de armario escaneado el OCR confunde designaciones con referencias de fabricante: solo sirve para la ubicación
                continue;
            }
            foreach (var t in pag.Trozos)
            {
                if ((t.Y0 + t.Y1) / 2 > pag.Alto * Pie) continue;     // cajetín
                string txt = t.Texto.Trim();
                // El OCR a veces junta designaciones vecinas: "Q21Q22Q23" -> Q21, Q22, Q23
                var partes = new List<string> { txt };
                if (pag.Origen == "ocr")
                {
                    var trozos = Regex.Split(txt, @"(?<=\d)(?=[A-Z])").Where(p => Rx.Completa(EquipoRe, p)).ToList();
                    if (trozos.Count > 1 && trozos.Select(p => p[0]).Distinct().Count() == 1)
                        partes = trozos;
                    // "Q100H": el OCR pega una letra del símbolo de al lado
                    var m = Rx.CompletaMatch(Rx.Crear(@"([A-Z]+\d+(?:\.\d+)?)[A-Z]"), txt);
                    if (m != null) partes = new List<string> { m.Groups[1].Value };
                }
                foreach (var parte in partes)
                {
                    bool esLetras = Rx.Completa(SoloLetrasRe, parte) && TiposPlaca.Contains(TipoEquipo(parte));
                    if ((Rx.Completa(EquipoRe, parte) || esLetras) && !NoEquipoRe.IsMatch(parte))
                    {
                        todos.Add((parte, t.Dir == Direccion.Horizontal, pag.Origen == "ocr"));
                        // Orden de lectura: página, de arriba abajo (por líneas) y de izquierda a derecha
                        var pos = (pag.Numero, R(t.Y0 / Linea), t.X0);
                        if (esPlaca && (!placaPos.TryGetValue(parte, out var pp) || t.Tam > pp.Tam))
                            placaPos[parte] = (t.Tam, pag.Numero, (t.Y0 + t.Y1) / 2, t.X0);
                        var destino = esArmario ? primeraArmario : primera;
                        if (!destino.TryGetValue(parte, out var actual) || CmpPos(pos, actual) < 0)
                            destino[parte] = pos;
                        candidatos.Add(new Candidato(parte, t.Tam, t.Estilo, t.Dir == Direccion.Horizontal, pag.Numero, pag.Origen == "ocr"));
                    }
                }
            }
        }
        if (candidatos.Count == 0) return new List<Elemento>();

        // Estilo de referencia = el que tiene más designaciones distintas (texto horizontal). Se calcula por
        // separado para páginas con texto y para páginas escaneadas (OCR).
        var paginasDe = new MapaOrdenado<string, SortedSet<int>>();
        void Anotar(string txt, int num)
        {
            if (!paginasDe.TryGetValue(txt, out var s)) { s = new SortedSet<int>(); paginasDe[txt] = s; }
            s.Add(num);
        }
        foreach (bool esOcr in new[] { false, true })
        {
            var grupo = candidatos.Where(c => c.EsOcr == esOcr).ToList();
            var estilos = new MapaOrdenado<(double, object?), HashSet<string>>();
            foreach (var c in grupo)
            {
                if (!c.Horiz) continue;
                var clave = esOcr ? (Math.Round(c.Tam / 2, MidpointRounding.ToEven) + 1e9, (object?)"ocr")
                                  : (Math.Round(c.Tam, 1, MidpointRounding.ToEven), c.Estilo);
                if (!estilos.TryGetValue(clave, out var set)) { set = new HashSet<string>(); estilos[clave] = set; }
                set.Add(c.Texto);
            }
            if (estilos.Count == 0) continue;
            var refClave = estilos.Claves[0];
            foreach (var k in estilos.Claves)
                if (estilos[k].Count > estilos[refClave].Count) refClave = k;
            if (esOcr)
            {
                // En una página escaneada no hay color ni tamaño fiable: se aceptan todas las designaciones y se quitan
                // cables (U101/V101/W101) y bornes de bobina (A1, A2).
                var textos = grupo.Select(c => c.Texto).ToHashSet();
                foreach (var c in grupo)
                    if (c.Horiz && !PareceCable(c.Texto, textos) && !Regex.IsMatch(c.Texto, @"\AA[12]\z"))
                        Anotar(c.Texto, c.Pagina);
                continue;
            }
            foreach (var c in grupo)
                if (Equals(c.Estilo, refClave.Item2) && Math.Abs(c.Tam - refClave.Item1) <= 0.3)
                    Anotar(c.Texto, c.Pagina);
        }
        if (paginasDe.Count == 0) return new List<Elemento>();

        // Los elementos que salen en la placa pero con otro tamaño/color (U30, U01, U150…) no se pierden:
        // se aceptan solo si el prefijo es de un tipo conocido.
        string placa = zonas["Placa"];
        var todosTextos = todos.Select(x => x.Texto).ToHashSet();
        foreach (var (txt, horiz, ocr) in todos)
        {
            if (!horiz || ocr || paginasDe.Contiene(txt)) continue;
            if (!TiposPlaca.Contains(TipoEquipo(txt))) continue;
            if (!Regex.IsMatch(txt, @"\A[A-Z]+\d+(?:\.\d+)?\z")) continue;
            var pref = Regex.Match(txt, @"\A[A-Z]+").Value;
            if (!Tipos.ContainsKey(pref)) continue;
            if (!Regex.IsMatch(placa, @"(?<![A-Z])" + Regex.Escape(txt) + @"(?![A-Z0-9]|\.[0-9])")) continue;
            if (PareceCable(txt, todosTextos)) continue;
            Anotar(txt, 0);
        }
        // Las de solo letras (RS, KMN…) deben figurar en la placa
        foreach (var d in paginasDe.Claves.Where(d => Rx.Completa(SoloLetrasRe, d)).ToList())
            if (!Regex.IsMatch(placa, @"(?<![A-Z])" + Regex.Escape(d) + @"(?![A-Z0-9])"))
                paginasDe.Quitar(d);

        bool EstaEn(string d, string texto) =>
            // KM100 no debe casar con M100, ni Q1 con Q100 / Q1.1
            Regex.IsMatch(texto, @"(?<![A-Z])" + Regex.Escape(d) + @"(?![0-9]|\.[0-9])");

        string Ubicacion(string d)
        {
            if (EstaEn(d, zonas["Placa"])) return "Placa";
            if (EstaEn(d, zonas["Puerta"])) return "Puerta";
            if (zonas["Placa"].Length == 0 && zonas["Puerta"].Length == 0) return "Sin plano de armario";
            // KM20.1 = segundo contactor del inversor KM20: va donde va KM20
            string baseD = d.Split('.')[0];
            if (baseD != d && EstaEn(baseD, zonas["Placa"])) return "Placa";
            // Estos elementos siempre van dentro del armario
            if (DentroArmario.Contains(TipoEquipo(d))) return "Placa";
            return "Fuera del armario";
        }

        // Orden de las etiquetas = el del plano de la placa (implantación): por filas de carriles, de arriba abajo,
        // y dentro de cada fila de izquierda a derecha. Los que no salen en ese plano van después, en orden de
        // lectura del esquema, y al final los que solo salen en armario.
        var fila = new Dictionary<(int, double), int>();
        foreach (var pagina in placaPos.Values.Select(v => v.Pagina).Distinct())
        {
            var ys = placaPos.Values.Where(v => v.Pagina == pagina).Select(v => v.Y).Distinct().OrderBy(y => y).ToList();
            int n = 0;
            for (int i = 0; i < ys.Count; i++)
            {
                if (i > 0 && ys[i] - ys[i - 1] > GapFila) n++;
                fila[(pagina, ys[i])] = n;
            }
        }

        (int Grupo, double A, double B, double C) Clave(string d)
        {
            if (placaPos.TryGetValue(d, out var pp))
                return (0, pp.Pagina, fila[(pp.Pagina, pp.Y)], pp.X);
            if (primera.TryGetValue(d, out var p1)) return (1, p1.Item1, p1.Item2, p1.Item3);
            if (primeraArmario.TryGetValue(d, out var p2)) return (2, p2.Item1, p2.Item2, p2.Item3);
            return (3, 0, 0, 0);
        }

        int CmpDesignaciones(string a, string b)
        {
            var ka = Clave(a); var kb = Clave(b);
            int c = ka.Grupo.CompareTo(kb.Grupo);
            if (c != 0) return c;
            c = CmpTupla3(new[] { ka.A, ka.B, ka.C }, new[] { kb.A, kb.B, kb.C });
            if (c != 0) return c;
            return CmpOrdenNatural(a, b);
        }

        // Las regletas de bornas (X1, X2, X10…) no llevan etiqueta de elemento: sus bornas ya salen en la pestaña Bornas.
        var claves = paginasDe.Claves.ToList();
        claves.Sort(CmpDesignaciones);
        return claves.Where(d => TipoEquipo(d) != Tipos["X"]).Select(d => new Elemento
        {
            Designacion = d, Tipo = TipoEquipo(d), Ubicacion = Ubicacion(d),
            Paginas = paginasDe[d].Select(x => (object)x).ToList(),
        }).ToList();
    }

    /// <summary>U101 junto a V101 y W101 (o PE101) es un número de cable/borna, no un elemento.</summary>
    private static bool PareceCable(string txt, ISet<string> textos)
    {
        var m = Rx.CompletaMatch(Rx.Crear(@"(U|V|W|PE)(\d+)"), txt);
        if (m == null) return false;
        string n = m.Groups[2].Value;
        return new[] { "U", "V", "W", "PE" }.Count(f => textos.Contains(f + n)) >= 2;
    }

    // --------------------------------------------------------------------------
    // Cables (hilos)
    // --------------------------------------------------------------------------
    // En el esquema cada hilo lleva una marca inclinada en el extremo; encima su número (negro) y debajo su
    // color (rojo): "L01 / NE", "060 / BL"… Con el dibujo vectorial del PDF se sigue la línea del hilo hasta
    // sus dos extremos (borna de un aparato, regleta, flecha de referencia a otra hoja…).
    private const string Colores = "NE|MA|AZ|GR|BL|VE|AM|RS|BU|NA|RO|BN|BK|GN|YE|RD|WH|OR|VI|GY|PK";
    public static readonly Regex ColorCableRe = Rx.Crear($"(?:{Colores})(?:/(?:{Colores}))?");
    // Hilos de fuerza (potencia): fases, neutro, tierra, salida a motor (L1, L01, N, PE, U0…).
    public static readonly Regex FuerzaRe = Rx.Crear(@"(?:[LN]\d{0,3}|PE\d*|[RSTUVW]\d{0,3}|\d?[LT]\d)", RegexOptions.IgnoreCase);
    private static readonly Regex DesignacionRe = Rx.Crear(@"(?:\+?-)?[A-Z][A-Z_]*\d*(?:\.\d+)?");
    private static readonly Rgb Azul = new(0.0, 0.0, 1.0);
    private const double TolPunto = 0.8;     // pt: dos extremos de línea a esta distancia son el mismo punto
    private const int MaxPasos = 400;        // tope de segmentos al seguir un hilo (evita bucles)
    private const double MaxHueco = 9.0;     // pt: hueco máximo entre dos trazos de una línea discontinua

    /// <summary>'Fuerza' si el número de hilo es de potencia (L1, L01, N, PE, U0, V0…); si no, 'Maniobra'.</summary>
    public static string TipoCable(string numero) => Rx.Completa(FuerzaRe, numero.Trim()) ? "Fuerza" : "Maniobra";

    /// <summary>Color entero de un texto (0xRRGGBB) -> (r, g, b) de 0 a 1.</summary>
    private static Rgb RgbDe(int c) => new(
        Math.Round(((c >> 16) & 255) / 255.0, 2, MidpointRounding.ToEven),
        Math.Round(((c >> 8) & 255) / 255.0, 2, MidpointRounding.ToEven),
        Math.Round((c & 255) / 255.0, 2, MidpointRounding.ToEven));

    private static double DistPuntoSeg(double px, double py, Trazo s)
    {
        double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
        double n = dx * dx + dy * dy;
        double t = n == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - s.X0) * dx + (py - s.Y0) * dy) / n));
        return Math.Sqrt(Sq(px - (s.X0 + t * dx)) + Sq(py - (s.Y0 + t * dy)));
    }

    private static double Sq(double v) => v * v;
    private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    /// <summary>Distancia de un punto a una caja.</summary>
    private static double DistCaja((double X, double Y) p, Circulo c) =>
        Hypot(Math.Max(Math.Max(c.X0 - p.X, 0), p.X - c.X1), Math.Max(Math.Max(c.Y0 - p.Y, 0), p.Y - c.Y1));

    /// <summary>Si el hilo acaba en un patillo de otro color (el terminal del aparato), sigue hasta su punta.</summary>
    private static (double X, double Y) Prolongar(List<Trazo> trazos, (double X, double Y) punto, (double X, double Y) sentido)
    {
        for (int rep = 0; rep < 3; rep++)
        {
            (double N, (double, double) P)? mejor = null;
            foreach (var o in trazos)
            {
                for (int ext = 0; ext < 2; ext++)
                {
                    double ex = ext == 0 ? o.X0 : o.X1, ey = ext == 0 ? o.Y0 : o.Y1;
                    if (Hypot(ex - punto.X, ey - punto.Y) > TolPunto) continue;
                    double fx = ext == 0 ? o.X1 : o.X0, fy = ext == 0 ? o.Y1 : o.Y0;
                    double vx = fx - ex, vy = fy - ey;
                    double n = Hypot(vx, vy);
                    if (!(1 < n && n <= 30)) continue;
                    if ((vx * sentido.X + vy * sentido.Y) / n < 0.97) continue;     // debe seguir en la misma dirección
                    if (mejor == null || n > mejor.Value.N) mejor = (n, (fx, fy));
                }
            }
            if (mejor == null) break;
            punto = mejor.Value.P;
        }
        return punto;
    }

    /// <summary>Distancia de un punto al rectángulo de un texto.</summary>
    private static double DistTexto(double px, double py, TextoPos t)
    {
        double dx = Math.Max(Math.Max(t.X0 - px, 0), px - t.X1);
        double dy = Math.Max(Math.Max(t.Y0 - py, 0), py - t.Y1);
        return Hypot(dx, dy);
    }

    /// <summary>Sigue un hilo desde un extremo de su segmento. Devuelve (punto final, empalme).</summary>
    private static ((double X, double Y) Punto, bool Empalme) Seguir(List<Trazo> trazos, List<Circulo> circulos, int inicio, int extremo)
    {
        var seg = trazos[inicio];
        var color = seg.Color;
        (double X, double Y) punto = extremo == 0 ? (seg.X0, seg.Y0) : (seg.X1, seg.Y1);
        var visto = new HashSet<int> { inicio };
        var segActual = seg;
        for (int paso = 0; paso < MaxPasos; paso++)
        {
            var vecinos = new List<int>();
            for (int i = 0; i < trazos.Count; i++)
            {
                var o = trazos[i];
                if (visto.Contains(i) || o.Color != color) continue;
                if (DistPuntoSeg(punto.X, punto.Y, o) <= TolPunto) vecinos.Add(i);
            }
            if (vecinos.Count > 1)
            {
                var largos = vecinos.Where(i => Hypot(trazos[i].X1 - trazos[i].X0, trazos[i].Y1 - trazos[i].Y0) > 6).ToList();
                if (largos.Count > 0 && largos.Count < vecinos.Count)
                {
                    foreach (var i in vecinos) if (!largos.Contains(i)) visto.Add(i);      // muescas de la esquina
                    vecinos = largos;
                }
            }
            if (vecinos.Count == 0)
            {
                if (circulos.Any(c => DistCaja(punto, c) <= 3.5))
                    return (punto, false);          // llega a una borna (circulito): aquí acaba el hilo
                // línea discontinua (manguera): se salta el hueco hasta el trazo siguiente, en la misma dirección
                var sig = SiguienteTrazo(trazos, visto, color, punto, segActual);
                if (sig == null) return (punto, false);
                visto.Add(sig.Value.Indice);
                segActual = trazos[sig.Value.Indice];
                punto = sig.Value.Otro;
                continue;
            }
            if (vecinos.Count > 1) return (punto, true);
            var oo = trazos[vecinos[0]];
            (double X, double Y) a = (oo.X0, oo.Y0), b = (oo.X1, oo.Y1);
            double da = Hypot(a.X - punto.X, a.Y - punto.Y), db = Hypot(b.X - punto.X, b.Y - punto.Y);
            if (Math.Min(da, db) > TolPunto) return (punto, true);     // el hilo toca el medio de otra línea (derivación en T)
            visto.Add(vecinos[0]);
            segActual = oo;
            punto = da <= db ? b : a;
        }
        return (punto, true);
    }

    /// <summary>Trazo del mismo color que continúa la línea (misma dirección) tras un hueco.</summary>
    private static (int Indice, (double X, double Y) Otro)? SiguienteTrazo(
        List<Trazo> trazos, HashSet<int> visto, Rgb color, (double X, double Y) punto, Trazo seg)
    {
        double dx = punto.X - (seg.X0 + seg.X1) / 2, dy = punto.Y - (seg.Y0 + seg.Y1) / 2;
        double n = Hypot(dx, dy);
        if (n == 0) return null;
        dx /= n; dy /= n;
        (double D, int I, (double, double) P)? mejor = null;
        for (int i = 0; i < trazos.Count; i++)
        {
            var o = trazos[i];
            if (visto.Contains(i) || o.Color != color) continue;
            for (int ext = 0; ext < 2; ext++)
            {
                double ex = ext == 0 ? o.X0 : o.X1, ey = ext == 0 ? o.Y0 : o.Y1;
                double vx = ex - punto.X, vy = ey - punto.Y;
                double d = Hypot(vx, vy);
                if (d < 0.01 || d > MaxHueco) continue;
                if ((vx * dx + vy * dy) / d < 0.97) continue;        // no sigue la misma dirección
                if (mejor == null || d < mejor.Value.D)
                    mejor = (d, i, ext == 0 ? (o.X1, o.Y1) : (o.X0, o.Y0));
            }
        }
        return mejor == null ? null : (mejor.Value.I, mejor.Value.P);
    }

    private sealed record Marco(double X0, double Y0, double X1, double Y1, string Nombre);

    /// <summary>Recuadros azules (los aparatos dibujados como caja) con el nombre que tienen a su izquierda.</summary>
    private static List<Marco> Marcos(Pagina pag, List<TextoPos> designaciones)
    {
        var azules = pag.Trazos.Where(s => s.Color == Azul && (Math.Abs(s.X0 - s.X1) < 0.3 || Math.Abs(s.Y0 - s.Y1) < 0.3)).ToList();
        var hor = azules.Where(s => Math.Abs(s.Y0 - s.Y1) < 0.3 && Math.Abs(s.X0 - s.X1) > 20)
                        .Select(s => (X0: Math.Min(s.X0, s.X1), X1: Math.Max(s.X0, s.X1), Y: s.Y0)).ToList();
        var ver = azules.Where(s => Math.Abs(s.X0 - s.X1) < 0.3 && Math.Abs(s.Y0 - s.Y1) > 20)
                        .Select(s => (X: s.X0, Y0: Math.Min(s.Y0, s.Y1), Y1: Math.Max(s.Y0, s.Y1))).ToList();
        var cajas = new List<(double X0, double Y0, double X1, double Y1)>();
        for (int i = 0; i < hor.Count; i++)
        {
            var (ax0, ax1, ay) = hor[i];
            for (int j = i + 1; j < hor.Count; j++)
            {
                var (bx0, bx1, by) = hor[j];
                if (Math.Abs(ax0 - bx0) > 0.6 || Math.Abs(ax1 - bx1) > 0.6 || Math.Abs(by - ay) < 20) continue;
                double y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
                bool izq = ver.Any(v => Math.Abs(v.X - ax0) <= 0.6 && v.Y0 <= y0 + 0.6 && v.Y1 >= y1 - 0.6);
                bool der = ver.Any(v => Math.Abs(v.X - ax1) <= 0.6 && v.Y0 <= y0 + 0.6 && v.Y1 >= y1 - 0.6);
                if (izq && der) cajas.Add((ax0, y0, ax1, y1));
            }
        }
        var res = new List<Marco>();
        foreach (var (x0, y0, x1, y1) in cajas)
        {
            if ((x1 - x0) * (y1 - y0) > pag.Ancho * pag.Alto * 0.5) continue;
            // nombre: justo a la izquierda del borde izquierdo, a la altura de la parte alta
            var cand = designaciones.Where(t => t.X1 <= x0 + 12 && t.X1 >= x0 - 80 && y0 - 15 <= t.Y0 && t.Y0 <= y0 + 40).ToList();
            if (cand.Count > 0)
            {
                var t = cand.MinBy(t => Math.Abs(t.Y0 - y0) + Math.Abs(x0 - t.X1))!;
                res.Add(new Marco(x0, y0, x1, y1, t.Texto.TrimStart('+', '-')));
            }
        }
        return res;
    }

    /// <summary>Nombre del aparato al que pertenece el punto: el recuadro con nombre más grande que lo contiene;
    /// si no hay, el nombre más cercano a la izquierda y arriba (o en la misma fila).</summary>
    private static string Dueno(List<Marco> marcos, List<TextoPos> designaciones, double cx, double cy)
    {
        var dentro = marcos.Where(m => m.X0 - 1 <= cx && cx <= m.X1 + 1 && m.Y0 - 1 <= cy && cy <= m.Y1 + 1).ToList();
        if (dentro.Count > 0) return dentro.MaxBy(m => (m.X1 - m.X0) * (m.Y1 - m.Y0))!.Nombre;
        var cerca = designaciones.Where(t => t.X0 <= cx && t.Y0 <= cy + 15 && DistTexto(cx, cy, t) <= 160).ToList();
        return cerca.Count > 0 ? cerca.MinBy(t => DistTexto(cx, cy, t))!.Texto.TrimStart('+', '-') : "";
    }

    private static bool EstiloEs(TextoPos t, int color) => t.Estilo is int c && c == color;

    /// <summary>Texto de un extremo del hilo. Devuelve (texto, dudoso).</summary>
    private static (string Texto, bool Dudoso) Extremo(
        Pagina pag, (double X, double Y) junta, (double X, double Y) punto, (double X, double Y) sentido,
        List<TextoPos> pines, List<TextoPos> designaciones, List<Marco> marcos)
    {
        double px = junta.X, py = junta.Y;
        // 1) flecha de referencia a otra hoja: "101 (hoja 93.7)"
        var cerca = pag.Flechas.Where(f => Hypot(f.X - px, f.Y - py) <= 9).ToList();
        if (cerca.Count > 0)
        {
            double fx = cerca[0].X, fy = cerca[0].Y;
            var grandes = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal && EstiloEs(t, 0x0000FF) && t.Tam >= 9 && DistTexto(fx, fy, t) <= 30).ToList();
            var hojas = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal && EstiloEs(t, 0x7CA552) && DistTexto(fx, fy, t) <= 30).ToList();
            string nombre = grandes.Count > 0 ? grandes.MinBy(t => DistTexto(fx, fy, t))!.Texto : "";
            string hoja = hojas.Count > 0 ? hojas.MinBy(t => DistTexto(fx, fy, t))!.Texto.Trim('/', ' ') : "";
            if (nombre.Length > 0 || hoja.Length > 0)
                return (nombre.Length > 0 && hoja.Length > 0 ? $"{nombre} (hoja {hoja})" : (nombre.Length > 0 ? nombre : $"hoja {hoja}"), false);
        }
        // 2) borna: número de pin junto al extremo, más allá de donde acaba la línea
        double qx = punto.X + sentido.X * 6, qy = punto.Y + sentido.Y * 6;
        double Lejos(TextoPos t) => Math.Min(Math.Min(DistTexto(px, py, t), DistTexto(punto.X, punto.Y, t)), DistTexto(qx, qy, t));
        var pinesCerca = pines.Where(t => Lejos(t) <= 14).ToList();
        TextoPos? pin = pinesCerca.Count > 0 ? pinesCerca.MinBy(Lejos) : null;
        double cx = pin != null ? (pin.X0 + pin.X1) / 2 : px;
        double cy = pin != null ? (pin.Y0 + pin.Y1) / 2 : py;
        string dueno = Dueno(marcos, designaciones, cx, cy);
        if (pin != null && dueno.Length > 0) return ($"{dueno}:{pin.Texto}", false);
        if (pin != null) return (pin.Texto, true);
        return dueno.Length > 0 ? (dueno, true) : ("", true);
    }

    /// <summary>
    /// Hilos del esquema. Solo en PDF con texto (hace falta el dibujo vectorial). Los hilos sin número
    /// (solo color, p. ej. las tierras) salen con número "".
    /// </summary>
    public static List<Cable> ExtraerCables(IEnumerable<Pagina> paginas)
    {
        var filas = new List<(Cable Cable, double Y, double X)>();
        var vistos = new HashSet<(int, string, string, string, string)>();
        foreach (var pag in paginas)
        {
            if (pag.Origen != "pdf" || pag.Trazos.Count == 0) continue;
            var trazos = pag.Trazos;
            var horizontales = pag.Trozos.Where(t => t.Dir == Direccion.Horizontal).ToList();
            var pines = horizontales.Where(t => 7.0 <= t.Tam && t.Tam <= 8.2 && EstiloEs(t, 0) && (t.Y0 + t.Y1) / 2 < pag.Alto * Pie).ToList();
            var designaciones = horizontales.Where(t => EstiloEs(t, 0x0000FF) && t.Tam >= 9 && t.Texto.Length <= 9
                                    && Rx.Completa(DesignacionRe, t.Texto)
                                    && (t.Texto[0] == '+' || t.Texto[0] == '-' || t.Texto[0] == 'X' || t.Texto.Any(char.IsDigit))).ToList();
            designaciones = designaciones.Where(t => !pag.Flechas.Any(f => DistTexto(f.X, f.Y, t) <= 30)).ToList();
            var marcos = Marcos(pag, designaciones);
            // marcas inclinadas: líneas cortas en diagonal
            var marcas = new List<int>();
            for (int i = 0; i < trazos.Count; i++)
            {
                var s = trazos[i];
                double len = Hypot(s.X1 - s.X0, s.Y1 - s.Y0);
                double ratio = Math.Abs(s.X1 - s.X0) / Math.Max(Math.Abs(s.Y1 - s.Y0), 0.01);
                if (5 <= len && len <= 11 && 0.6 <= ratio && ratio <= 1.6) marcas.Add(i);
            }
            var marcasSet = marcas.ToHashSet();
            foreach (var ct in horizontales)
            {
                if (!Rx.Completa(ColorCableRe, ct.Texto) || !(7.0 <= ct.Tam && ct.Tam <= 10.5)) continue;
                if ((ct.Y0 + ct.Y1) / 2 > pag.Alto * Pie) continue;
                Rgb? colRgb = ct.Estilo is int ci ? RgbDe(ci) : null;
                // número del hilo: texto justo encima del color, en la misma columna
                var encima = horizontales.Where(t => !ReferenceEquals(t, ct) && Math.Abs(t.Tam - ct.Tam) <= 0.3 && !Equals(t.Estilo, ct.Estilo)
                                                    && Math.Abs(t.X0 - ct.X0) <= 3 && 0 < ct.Y0 - t.Y0 && ct.Y0 - t.Y0 <= ct.Tam * 1.7).ToList();
                string numero = encima.Count > 0 ? encima.MinBy(t => ct.Y0 - t.Y0)!.Texto : "";
                // marca inclinada a la izquierda del color
                double mx = ct.X0 - 2, my = (ct.Y0 + ct.Y1) / 2 + 3;
                var tick = marcas.Where(i => colRgb != null && trazos[i].Color == colRgb.Value
                                              && Hypot((trazos[i].X0 + trazos[i].X1) / 2 - mx, (trazos[i].Y0 + trazos[i].Y1) / 2 - my) <= 12).ToList();
                string origen = "", destino = "";
                bool dudoso = true;
                if (tick.Count > 0)
                {
                    var t = trazos[tick[0]];
                    double tx = (t.X0 + t.X1) / 2, ty = (t.Y0 + t.Y1) / 2;
                    var hilos = new List<int>();
                    for (int i = 0; i < trazos.Count; i++)
                    {
                        var s = trazos[i];
                        if (marcasSet.Contains(i)) continue;
                        if (Hypot(s.X1 - s.X0, s.Y1 - s.Y0) <= 12) continue;
                        if (!(Math.Abs(s.X0 - s.X1) < 0.3 || Math.Abs(s.Y0 - s.Y1) < 0.3)) continue;
                        if (DistPuntoSeg(tx, ty, s) <= 1.0) hilos.Add(i);
                    }
                    if (hilos.Count > 0)
                    {
                        // el hilo no es azul (el azul es el dibujo de los aparatos y los subrayados de texto)
                        int h = hilos.OrderBy(i => trazos[i].Color == Azul ? 1 : 0).ThenBy(i => DistPuntoSeg(tx, ty, trazos[i])).First();
                        var extremos = new List<(double Y, double X, string Texto, bool Dud)>();
                        for (int lado = 0; lado < 2; lado++)
                        {
                            var (punto, empalme) = Seguir(trazos, pag.Circulos, h, lado);
                            var s = trazos[h];
                            (double X, double Y) otro = lado == 0 ? (s.X1, s.Y1) : (s.X0, s.Y0);
                            // sentido de salida: desde el interior del hilo hacia el extremo
                            double vx = punto.X - otro.X, vy = punto.Y - otro.Y;
                            double n = Hypot(vx, vy);
                            if (n == 0) n = 1;
                            var sentido = (vx / n, vy / n);
                            var punta = Prolongar(trazos, punto, sentido);
                            var (texto, dud) = Extremo(pag, punto, punta, sentido, pines, designaciones, marcos);
                            extremos.Add((punto.Y, punto.X, texto, dud || empalme));
                        }
                        extremos = extremos.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Texto, StringComparer.Ordinal).ThenBy(e => e.Dud).ToList();
                        origen = extremos[0].Texto; destino = extremos[1].Texto;
                        dudoso = extremos[0].Dud || extremos[1].Dud || origen.Length == 0 || destino.Length == 0;
                    }
                }
                if (numero.Length > 0)
                {
                    var clave = (pag.Numero, numero, ct.Texto, origen, destino);
                    if (!vistos.Add(clave)) continue;
                }
                filas.Add((new Cable
                {
                    Numero = numero, Color = ct.Texto, Tipo = numero.Length > 0 ? TipoCable(numero) : "",
                    Origen = origen, Destino = destino, Dudoso = dudoso, Pagina = pag.Numero,
                }, ct.Y0, ct.X0));
            }
        }
        // Un hilo con número "raro" (los hilos 1, 2, 3 de la manguera de un motor) que llega al mismo extremo
        // (regleta:borna) que un hilo de fuerza (U0, V0, W0…) también es de fuerza.
        var extremosFuerza = new HashSet<(int, string)>();
        foreach (var (f, _, _) in filas)
            if (f.Tipo == "Fuerza")
                foreach (var e in new[] { f.Origen, f.Destino })
                    if (e.Contains(':')) extremosFuerza.Add((f.Pagina, e));
        foreach (var (f, _, _) in filas)
            if (f.Tipo == "Maniobra" && new[] { f.Origen, f.Destino }.Any(e => e.Contains(':') && extremosFuerza.Contains((f.Pagina, e))))
                f.Tipo = "Fuerza";
        // Orden de lectura: página, de arriba abajo (por líneas) y de izquierda a derecha
        return filas.OrderBy(f => f.Cable.Pagina).ThenBy(f => R(f.Y / Linea)).ThenBy(f => f.X).Select(f => f.Cable).ToList();
    }

    // --------------------------------------------------------------------------
    // Tablas (Excel / CSV)
    // --------------------------------------------------------------------------
    // Palabras de cabecera habituales en los listados de EPLAN, SEE, AutoCAD Electrical…
    private static readonly Dictionary<string, string[]> Claves = new()
    {
        ["regleta"] = new[] { "regleta", "bornero", "bornier", "terminal strip", "strip", "klemmleiste", "regleta de bornes" },
        ["borna"] = new[] { "borna", "borne", "terminal", "klemme", "marca", "conexión", "conexion", "nº borna", "n° borne" },
        ["designacion"] = new[] { "designación", "designacion", "aparato", "equipo", "elemento", "dispositivo", "device",
                                  "tag", "identificador", "bmk", "repère", "repere", "référence d'appareil" },
        ["descripcion"] = new[] { "descripción", "descripcion", "función", "funcion", "description", "fonction",
                                  "texto", "comentario", "beschreibung" },
    };

    private static int Puntuar(string celda, string campo)
    {
        string c = celda.ToLowerInvariant().Trim();
        if (c.Length == 0) return 0;
        foreach (var k in Claves[campo]) if (c == k) return 3;
        return Claves[campo].Any(k => c.Contains(k, StringComparison.Ordinal)) ? 1 : 0;
    }

    /// <summary>Adivina la fila de cabecera, las columnas y si es un listado de bornas o de elementos.</summary>
    public static AnalisisTabla AnalizarTabla(IReadOnlyList<IReadOnlyList<string>> filas)
    {
        (int P, int? I) mejor = (0, null);
        for (int i = 0; i < Math.Min(15, filas.Count); i++)
        {
            int p = filas[i].Sum(c => Claves.Keys.Max(campo => Puntuar(c, campo)));
            if (p > mejor.P) mejor = (p, i);
        }
        int? cab = mejor.P >= 2 ? mejor.I : null;
        var columnas = new Dictionary<string, int?>();
        if (cab != null)
        {
            var usadas = new HashSet<int>();
            foreach (var campo in new[] { "regleta", "designacion", "borna", "descripcion" })
            {
                // max((puntuación, índice)): en caso de empate gana el índice mayor
                (int P, int J)? best = null;
                for (int j = 0; j < filas[cab.Value].Count; j++)
                {
                    if (usadas.Contains(j)) continue;
                    var cand = (P: Puntuar(filas[cab.Value][j], campo), J: j);
                    if (best == null || cand.P > best.Value.P || (cand.P == best.Value.P && cand.J > best.Value.J)) best = cand;
                }
                int p = best?.P ?? 0;
                columnas[campo] = p > 0 ? best!.Value.J : null;
                if (p > 0) usadas.Add(best!.Value.J);
            }
        }
        else
        {
            foreach (var k in Claves.Keys) columnas[k] = null;
        }
        var datos = (cab != null ? filas.Skip(cab.Value + 1) : filas).Where(f => f.Any(c => c.Length > 0)).ToList();
        // Sin cabecera: buscar una columna con marcas tipo "X1:5" o designaciones tipo KM100
        if (columnas.GetValueOrDefault("borna") == null && columnas.GetValueOrDefault("designacion") == null && datos.Count > 0)
        {
            int nCol = datos[0].Count;
            for (int j = 0; j < nCol; j++)
            {
                var vals = datos.Take(200).Select(f => j < f.Count ? f[j] : "").Where(v => v.Length > 0).ToList();
                if (vals.Count == 0) continue;
                if (vals.Count(v => Regex.IsMatch(v, @"\A\S+:\S+\z")) > vals.Count * 0.6)
                {
                    columnas["borna"] = j;
                    break;
                }
                if (vals.Count(v => EquipoRe.IsMatch(UltimaParte(v))) > vals.Count * 0.6)
                    columnas["designacion"] = j;
            }
        }
        string modo = columnas.GetValueOrDefault("borna") != null &&
                      (columnas.GetValueOrDefault("regleta") != null || columnas.GetValueOrDefault("designacion") == null)
            ? "bornas" : "elementos";
        return new AnalisisTabla(cab, columnas, modo);
    }

    /// <summary>'=ALM+ET-KM100' -> 'KM100';  '-Q1' -> 'Q1'.</summary>
    public static string UltimaParte(string v)
    {
        v = v.Trim();
        if (Regex.IsMatch(v, @"[-=+]"))
        {
            var partes = Regex.Split(v, @"[-=+]");
            return partes[^1].Trim();
        }
        return v;
    }

    /// <summary>Convierte la tabla en filas de bornas o de elementos.</summary>
    public static (List<Borna> Bornas, List<Elemento> Elementos) FilasDesdeTabla(
        IReadOnlyList<IReadOnlyList<string>> filas, int? cabecera, IReadOnlyDictionary<string, int?> columnas, string modo)
    {
        var datos = cabecera != null ? filas.Skip(cabecera.Value + 1).ToList() : filas.ToList();
        int inicio = (cabecera ?? -1) + 2;

        string Col(IReadOnlyList<string> fila, string campo)
        {
            columnas.TryGetValue(campo, out var j);
            return j != null && j.Value < fila.Count ? fila[j.Value].Trim() : "";
        }

        if (modo == "bornas")
        {
            var res = new List<Borna>();
            var contador = new Dictionary<string, int>();
            int n = inicio;
            foreach (var f in datos)
            {
                int fila = n++;
                if (!f.Any(c => c.Length > 0)) continue;
                string marca = Col(f, "borna"), regleta = Col(f, "regleta");
                if (marca.Length == 0) continue;
                if (regleta.Length == 0 && marca.Contains(':'))          // "X1:5" -> regleta X1, borna 5
                {
                    int i = marca.IndexOf(':');
                    regleta = marca[..i]; marca = marca[(i + 1)..];
                }
                regleta = UltimaParte(regleta);
                if (regleta.Length == 0) regleta = "—";
                contador[regleta] = contador.GetValueOrDefault(regleta) + 1;
                res.Add(new Borna
                {
                    Regleta = regleta, Pos = contador[regleta], Marca = marca,
                    Descripcion = Col(f, "descripcion"), Pagina = $"fila {fila}",
                });
            }
            return (res, new List<Elemento>());
        }

        var vistos = new MapaOrdenado<string, int>();
        int m = inicio;
        foreach (var f in datos)
        {
            int fila = m++;
            string d = UltimaParte(Col(f, "designacion"));
            if (d.Length > 0 && !vistos.Contiene(d)) vistos[d] = fila;
        }
        var lista = vistos.Pares().ToList();
        lista.Sort((a, b) => CmpOrdenNatural(a.Key, b.Key));
        return (new List<Borna>(), lista.Select(kv => new Elemento
        {
            Designacion = kv.Key, Tipo = TipoEquipo(kv.Key), Ubicacion = "Sin plano de armario",
            Paginas = new List<object> { $"fila {kv.Value}" },
        }).ToList());
    }

    // --------------------------------------------------------------------------
    // Número del esquema (para separar las etiquetas de cada esquema)
    // --------------------------------------------------------------------------
    private static readonly Regex EtiqNumeroRe = Rx.Crear(
        @"N\s*[º°oO]\.?\s*(?:DE\s+INSTALACI[ÓO]N|D'INSTALLATION|DE\s+PROYECTO|PROYECTO|PROJECT)", RegexOptions.IgnoreCase);
    private static readonly Regex ValorNumeroRe = Rx.Crear(@"[A-Z0-9][A-Z0-9_\-./]{3,}", RegexOptions.IgnoreCase);

    /// <summary>Número de instalación/proyecto del cajetín ("Nº DE INSTALACIÓN: CECE00000000001"), o null.</summary>
    public static string? NumeroEsquema(IReadOnlyList<Pagina> paginas)
    {
        foreach (var pag in paginas.Take(5))
        {
            foreach (var t in pag.Trozos)
            {
                var m = EtiqNumeroRe.Match(t.Texto);
                if (!m.Success) continue;
                // el número puede ir en el mismo texto ("...: CECE…") o en el texto de su derecha
                string resto = t.Texto[(m.Index + m.Length)..].Trim(' ', ':');
                if (Rx.Completa(ValorNumeroRe, resto.Length > 0 ? resto : "-")) return resto;
                double alto = Math.Max(t.Y1 - t.Y0, 1);
                var derecha = pag.Trozos.Where(o => !ReferenceEquals(o, t) && o.X0 >= t.X1 - 1
                                                    && Math.Abs((o.Y0 + o.Y1) / 2 - (t.Y0 + t.Y1) / 2) < alto * 0.6).OrderBy(o => o.X0);
                foreach (var o in derecha)
                    if (Rx.Completa(ValorNumeroRe, o.Texto.Trim())) return o.Texto.Trim();
            }
        }
        return null;
    }

    /// <summary>Una etiqueta por cada extremo del hilo: cada cable sale dos veces seguidas, con extremo "1/2" y "2/2".</summary>
    public static List<Cable> PorExtremos(IEnumerable<Cable> cables)
    {
        var salida = new List<Cable>();
        foreach (var c in cables)
        {
            var a = c.Clonar(); a.Extremo = "1/2"; salida.Add(a);
            var b = c.Clonar(); b.Extremo = "2/2"; salida.Add(b);
        }
        return salida;
    }

    // --------------------------------------------------------------------------
    // Copias por esquema
    // --------------------------------------------------------------------------
    /// <summary>
    /// Repite las etiquetas de cada esquema tantas veces como diga <paramref name="copias"/> {esquema: n}, seguidas:
    /// esquema A copia 1, A copia 2, A copia 3, esquema B copia 1… Cada fila resultante lleva Copia = "k/n".
    /// </summary>
    public static List<T> Multiplicar<T>(IEnumerable<T> filas, IReadOnlyDictionary<string, int> copias) where T : class, IFila
    {
        var orden = new List<string>();
        var porEsquema = new Dictionary<string, List<T>>();
        foreach (var f in filas)
        {
            string e = f.Esquema ?? "";
            if (!porEsquema.TryGetValue(e, out var lista))
            {
                orden.Add(e);
                porEsquema[e] = lista = new List<T>();
            }
            lista.Add(f);
        }
        var salida = new List<T>();
        foreach (var e in orden)
        {
            int n = Math.Max(1, copias.TryGetValue(e, out var v) ? v : 1);
            for (int k = 1; k <= n; k++)
                foreach (var f in porEsquema[e])
                {
                    var c = (T)f.Duplicar();
                    c.Copia = $"{k}/{n}";
                    salida.Add(c);
                }
        }
        return salida;
    }
}
