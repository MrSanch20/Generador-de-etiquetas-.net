using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Etiquetador.App;

/// <summary>Columna de una tabla: título, ancho (píxeles), alineación y si ocupa el espacio que sobra.</summary>
public sealed record ColumnaTabla(string Titulo, double Ancho, bool Derecha = false, bool Estirar = false);

/// <summary>Una fila ya preparada para pintar.</summary>
public sealed class FilaVista
{
    public string[] Vals { get; init; } = Array.Empty<string>();
    /// <summary>Fila verde (tierra / PE).</summary>
    public bool Pe { get; init; }
    /// <summary>Fila gris (su grupo está desmarcado).</summary>
    public bool Off { get; init; }
    /// <summary>La fila de datos a la que corresponde.</summary>
    public object? Origen { get; init; }
}

/// <summary>Tabla sencilla: cabecera fija y lista con selección múltiple (Ctrl/Mayús + clic, Ctrl+E selecciona todo).</summary>
public sealed class TablaView : Grid
{
    private static readonly IBrush FondoPe = new SolidColorBrush(Color.Parse("#eef7e6"));
    private static readonly IBrush TextoOff = new SolidColorBrush(Color.Parse("#9aa3ad"));
    private static readonly IBrush FondoCabecera = new SolidColorBrush(Color.Parse("#c9dcf0"));

    private readonly string _columnas;
    private readonly IReadOnlyList<ColumnaTabla> _cols;

    public ListBox Lista { get; private set; }

    public TablaView(IReadOnlyList<ColumnaTabla> columnas)
    {
        _cols = columnas;
        _columnas = string.Join(",", columnas.Select(c => c.Estirar ? "*" : ((int)c.Ancho).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        RowDefinitions = new RowDefinitions("Auto,*");

        // cabecera
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions(_columnas) };
        for (int i = 0; i < columnas.Count; i++)
            cab.Children.Add(Celda(columnas[i].Titulo, i, columnas[i].Derecha, negrita: true));
        var marco = new Border
        {
            Background = FondoCabecera,
            BorderBrush = new SolidColorBrush(Color.Parse("#d0d5dc")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 0, 20, 0),      // 20 = hueco de la barra de desplazamiento
            Child = cab,
        };
        SetRow(marco, 0);
        Children.Add(marco);

        Lista = CrearLista();
        SetRow(Lista, 1);
        Children.Add(Lista);
    }

    private ListBox CrearLista()
    {
        var lista = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<FilaVista>((fila, _) => Fila(fila), false),
        };
        lista.KeyDown += (_, e) =>
        {
            if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
            {
                lista.SelectAll();
                e.Handled = true;
            }
        };
        SetRow(lista, 1);
        return lista;
    }

    private static TextBlock Celda(string texto, int col, bool derecha, bool negrita = false, bool off = false)
    {
        // Una celda con un texto larguísimo (p. ej. un elemento que sale en miles de filas) bloquea el dibujado:
        // se enseña recortada y el texto completo sale al pasar el ratón.
        const int MaxCaracteres = 160;
        bool largo = texto.Length > MaxCaracteres;
        var t = new TextBlock
        {
            Text = largo ? texto[..MaxCaracteres] + "…" : texto,
            MaxLines = 1,
            Margin = new Thickness(4, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = derecha ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            FontWeight = negrita ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (off) t.Foreground = TextoOff;
        if (largo) ToolTip.SetTip(t, texto.Length > 2000 ? texto[..2000] + "…" : texto);
        Grid.SetColumn(t, col);
        return t;
    }

    private Control Fila(FilaVista fila)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(_columnas) };
        for (int i = 0; i < _cols.Count && i < fila.Vals.Length; i++)
            g.Children.Add(Celda(fila.Vals[i], i, _cols[i].Derecha, off: fila.Off));
        return new Border { Background = fila.Pe ? FondoPe : Brushes.Transparent, Child = g };
    }

    /// <summary>
    /// Pone las filas. Cambiar el contenido de una lista que ya está dibujada cerraba el programa (Avalonia 11.2.3),
    /// así que se monta una lista nueva y se sustituye la anterior.
    /// </summary>
    public void Poner(IReadOnlyList<FilaVista> filas)
    {
        var nueva = CrearLista();
        nueva.ItemsSource = filas;
        int pos = Children.IndexOf(Lista);
        if (pos >= 0) Children[pos] = nueva; else Children.Add(nueva);
        Lista = nueva;
    }

    /// <summary>Filas seleccionadas (su dato de origen).</summary>
    public List<T> Seleccion<T>() where T : class =>
        Lista.SelectedItems?.OfType<FilaVista>().Select(f => f.Origen).OfType<T>().ToList() ?? new List<T>();

    /// <summary>Filas visibles (su dato de origen).</summary>
    public List<T> Visibles<T>() where T : class =>
        (Lista.ItemsSource as IEnumerable<FilaVista>)?.Select(f => f.Origen).OfType<T>().ToList() ?? new List<T>();

    public void Reseleccionar(IEnumerable<object> origenes)
    {
        var set = origenes.ToHashSet();
        if (Lista.ItemsSource is not IEnumerable<FilaVista> filas) return;
        foreach (var f in filas)
            if (f.Origen != null && set.Contains(f.Origen)) Lista.SelectedItems?.Add(f);
    }
}
