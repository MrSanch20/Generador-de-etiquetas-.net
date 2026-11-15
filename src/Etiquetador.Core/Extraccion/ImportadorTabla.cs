using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Resultado de analizar una tabla (Excel/CSV).</summary>
public sealed record AnalisisTabla(int? Cabecera, Dictionary<string, int?> Columnas, string Modo);

/// <summary>Interpreta listados de Excel/CSV (EPLAN, SEE, AutoCAD Electrical…): detecta cabecera y columnas y saca bornas o elementos.</summary>
public static class ImportadorTabla
{
    // Tablas (Excel / CSV)
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
    public static AnalisisTabla Analizar(IReadOnlyList<IReadOnlyList<string>> filas)
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
                if (vals.Count(v => Designaciones.EquipoRe.IsMatch(UltimaParte(v))) > vals.Count * 0.6)
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
    public static (List<Borna> Bornas, List<Elemento> Elementos) FilasDesde(
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
        lista.Sort((a, b) => Designaciones.CmpOrdenNatural(a.Key, b.Key));
        return (new List<Borna>(), lista.Select(kv => new Elemento
        {
            Designacion = kv.Key, Tipo = Designaciones.TipoEquipo(kv.Key), Ubicacion = "Sin plano de armario",
            Paginas = new List<object> { $"fila {kv.Value}" },
        }).ToList());
    }
}
