using Avalonia;
using Avalonia.Controls;

namespace Etiquetador.App;

/// <summary>Pestaña «Todo»: bornas, elementos y cables juntos (solo lectura; usa los filtros de las otras pestañas).</summary>
public sealed class VistaTodo : Grid
{
    private readonly Sesion _s;
    private readonly TablaView _tabla;

    public VistaTodo(Sesion sesion)
    {
        _s = sesion;
        RowDefinitions = new RowDefinitions("Auto,*");
        Margin = new Thickness(0, 8, 0, 0);
        _tabla = new TablaView(new[]
        {
            new ColumnaTabla("Esquema", 150), new ColumnaTabla("Etiqueta", 100), new ColumnaTabla("Texto", 110),
            new ColumnaTabla("Regleta / tipo", 190), new ColumnaTabla("Descripción", 260, Estirar: true),
            new ColumnaTabla("Pág.", 90), new ColumnaTabla("SW-CODE", 80),
        });
        var nota = new TextBlock { Text = "Bornas, elementos y cables juntos. Usa los filtros de las otras pestañas.", Foreground = Dialogos.Gris, Margin = new Thickness(0, 0, 0, 6) };
        SetRow(nota, 0); SetRow(_tabla, 1);
        Children.Add(nota); Children.Add(_tabla);
    }

    public void Pintar(string q)
    {
        var filas = new List<FilaVista>();
        void Anadir(string[] vals)
        {
            if (q.Length == 0 || vals.Any(v => v.ToLowerInvariant().Contains(q, StringComparison.Ordinal)))
                filas.Add(new FilaVista { Vals = vals });
        }
        foreach (var f in _s.Bornas.Incluidas())
            Anadir(new[] { f.Esquema, "Borna", f.Marca, f.Regleta, f.Descripcion, Convert.ToString(f.Pagina) ?? "", f.SwCode });
        foreach (var e in _s.Elementos.Incluidas())
            Anadir(new[] { e.Esquema, "Elemento", e.Designacion, e.Tipo, "", e.PaginasTexto, e.SwCode });
        foreach (var (clase, nombre) in new[] { ("fuerza", "Cable fuerza"), ("maniobra", "Cable maniobra") })
            foreach (var c in _s.Cables(clase).Incluidas())
                Anadir(new[] { c.Esquema, nombre, c.Numero, c.Color, "", c.Pagina.ToString(), c.SwCode });
        _tabla.Poner(filas);
    }
}
