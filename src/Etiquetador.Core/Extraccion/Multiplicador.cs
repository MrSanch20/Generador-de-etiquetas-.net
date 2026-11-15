
namespace Etiquetador.Core.Extraccion;

/// <summary>Repite las etiquetas: una por cada extremo de un hilo y una por cada copia de un esquema.</summary>
public static class Multiplicador
{
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

    // Copias por esquema
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
