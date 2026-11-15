using System.Globalization;
using System.Text.RegularExpressions;

namespace Etiquetador.Core.Extraccion;

/// <summary>Bornas: páginas "Plano de bornes =...-XALI" -> marca de cada borna de cada regleta.</summary>
public static class ExtractorBornas
{
    // Bornas
    public static List<Borna> Extraer(IEnumerable<Pagina> paginas)
    {
        var filas = new List<Borna>();
        var contador = new Dictionary<string, int>();
        foreach (var pag in paginas)
        {
            var regleta = TitulosPagina.RegletaDe(pag);
            if (string.IsNullOrEmpty(regleta)) continue;     // no es un plano de bornes
            var verticales = pag.Lineas.Where(t => t.Dir == Direccion.Vertical && t.Y1 < pag.Alto * Medidas.Pie).ToList();
            if (verticales.Count == 0) continue;
            // Las marcas son la fila de textos verticales más alta (justo encima de la regleta dibujada);
            // el resto, más abajo, son descripciones.
            double tam = verticales.Select(t => t.Tam).OrderBy(x => x).ToList()[verticales.Count / 2];
            double tol; int yMarcas;
            if (pag.Origen == "pdf")
            {
                tol = 2;
                yMarcas = verticales.Min(t => Redondeo.R(t.Y1));
            }
            else
            {
                // escaneado/DXF: las marcas no están perfectamente alineadas; se toma la fila más alta que tenga bastantes textos
                tol = Math.Max(2, tam * 1.2);
                var vecinos = new MapaOrdenado<int, int>();
                foreach (var t in verticales)
                    vecinos[Redondeo.R(t.Y1)] = verticales.Count(o => Math.Abs(o.Y1 - t.Y1) <= tol);
                int mejor = vecinos.Pares().Max(p => p.Value);
                double umbral = Math.Max(2, mejor * 0.5);
                yMarcas = vecinos.Pares().Where(p => p.Value >= umbral).Min(p => p.Key);
            }
            var marcas = verticales.Where(t => Math.Abs(Redondeo.R(t.Y1) - yMarcas) <= tol).OrderBy(t => t.X0).ToList();
            var desc = verticales.Where(t => Redondeo.R(t.Y1) > yMarcas + 50).Select(t => ((t.X0 + t.X1) / 2, t.Texto)).ToList();
            foreach (var t in marcas)
            {
                double cx = t.X0 + 5;
                var cerca = desc.Where(d => Math.Abs(d.Item1 - cx) <= Medidas.MaxDistDescripcion).ToList();
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
}
