using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Elementos: designaciones del esquema (Q100, KM100, K20…) montados en la placa.</summary>
public static class ExtractorElementos
{
    // Palabras del título de las páginas de armario (placa / puerta)
    private static readonly (string Zona, string[] Palabras)[] Zonas =
    {
        ("Placa", new[] { "PLACA", "PLAQUE", "MOUNTING PLATE", "MONTAGEPLATTE" }),
        ("Puerta", new[] { "PUERTA", "PORTE", "DOOR", "TÜR", "TUER" }),
    };

    /// <summary>
    /// Solo los elementos cableados en la placa (KM, Q, K, F…). Los de la puerta y los de fuera del
    /// armario (motores, finales de carrera…) no llevan etiqueta. Si no hay plano de placa, se quitan los tipos
    /// que van en la puerta o fuera.
    /// </summary>
    public static List<Elemento> SoloPlaca(IEnumerable<Elemento> equipos)
    {
        var lista = equipos.Where(e => e.Tipo != Designaciones.Tipos["X"]).ToList();   // sin regletas de bornas
        if (lista.Any(e => e.Ubicacion == "Placa"))
            return lista.Where(e => e.Ubicacion == "Placa").ToList();
        return lista.Where(e => !Designaciones.FueraOPuerta.Contains(e.Tipo)).ToList();
    }

    // Elementos
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
    public static List<Elemento> Extraer(IEnumerable<Pagina> paginasEntrada)
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
            string pie = pag.TextoZona(pag.Alto * Medidas.Pie);
            string cuerpo = pag.TextoZona(0, pag.Alto * Medidas.Pie);
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
            if (TitulosPagina.RegletaDe(pag) != null) continue;   // los planos de bornes no tienen elementos
            if (esArmario && pag.Origen == "ocr")
            {
                // En un plano de armario escaneado el OCR confunde designaciones con referencias de fabricante: solo sirve para la ubicación
                continue;
            }
            foreach (var t in pag.Trozos)
            {
                if ((t.Y0 + t.Y1) / 2 > pag.Alto * Medidas.Pie) continue;     // cajetín
                string txt = t.Texto.Trim();
                // El OCR a veces junta designaciones vecinas: "Q21Q22Q23" -> Q21, Q22, Q23
                var partes = new List<string> { txt };
                if (pag.Origen == "ocr")
                {
                    var trozos = Regex.Split(txt, @"(?<=\d)(?=[A-Z])").Where(p => Rx.Completa(Designaciones.EquipoRe, p)).ToList();
                    if (trozos.Count > 1 && trozos.Select(p => p[0]).Distinct().Count() == 1)
                        partes = trozos;
                    // "Q100H": el OCR pega una letra del símbolo de al lado
                    var m = Rx.CompletaMatch(Rx.Crear(@"([A-Z]+\d+(?:\.\d+)?)[A-Z]"), txt);
                    if (m != null) partes = new List<string> { m.Groups[1].Value };
                }
                foreach (var parte in partes)
                {
                    bool esLetras = Rx.Completa(Designaciones.SoloLetrasRe, parte) && Designaciones.TiposPlaca.Contains(Designaciones.TipoEquipo(parte));
                    if ((Rx.Completa(Designaciones.EquipoRe, parte) || esLetras) && !Designaciones.NoEquipoRe.IsMatch(parte))
                    {
                        todos.Add((parte, t.Dir == Direccion.Horizontal, pag.Origen == "ocr"));
                        // Orden de lectura: página, de arriba abajo (por líneas) y de izquierda a derecha
                        var pos = (pag.Numero, Redondeo.R(t.Y0 / Medidas.Linea), t.X0);
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
            if (!Designaciones.TiposPlaca.Contains(Designaciones.TipoEquipo(txt))) continue;
            if (!Regex.IsMatch(txt, @"\A[A-Z]+\d+(?:\.\d+)?\z")) continue;
            var pref = Regex.Match(txt, @"\A[A-Z]+").Value;
            if (!Designaciones.Tipos.ContainsKey(pref)) continue;
            if (!Regex.IsMatch(placa, @"(?<![A-Z])" + Regex.Escape(txt) + @"(?![A-Z0-9]|\.[0-9])")) continue;
            if (PareceCable(txt, todosTextos)) continue;
            Anotar(txt, 0);
        }
        // Las de solo letras (RS, KMN…) deben figurar en la placa
        foreach (var d in paginasDe.Claves.Where(d => Rx.Completa(Designaciones.SoloLetrasRe, d)).ToList())
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
            if (Designaciones.DentroArmario.Contains(Designaciones.TipoEquipo(d))) return "Placa";
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
                if (i > 0 && ys[i] - ys[i - 1] > Medidas.GapFila) n++;
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
            return Designaciones.CmpOrdenNatural(a, b);
        }

        // Las regletas de bornas (X1, X2, X10…) no llevan etiqueta de elemento: sus bornas ya salen en la pestaña Bornas.
        var claves = paginasDe.Claves.ToList();
        claves.Sort(CmpDesignaciones);
        return claves.Where(d => Designaciones.TipoEquipo(d) != Designaciones.Tipos["X"]).Select(d => new Elemento
        {
            Designacion = d, Tipo = Designaciones.TipoEquipo(d), Ubicacion = Ubicacion(d),
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
}
