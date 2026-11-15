using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Etiquetador.App;

/// <summary>Pestaña con los filtros a la izquierda (casillas por grupo) y la tabla a la derecha.</summary>
public sealed class ListaView<T> : Grid where T : class, IFila
{
    private readonly ListaEtiquetas<T> _lista;
    private readonly Action _alCambiar;
    private readonly Action<List<T>, string> _alPonerSwcode;
    private readonly Func<string> _busqueda;
    private readonly Func<string, Task> _aviso;
    private readonly StackPanel _filtros = new() { Spacing = 2 };
    private bool _refrescando;

    public TablaView Tabla { get; }
    public TextBox Swcode { get; } = new() { Width = 90 };

    /// <param name="botonExtra">Botón de la derecha (texto, acción con las filas seleccionadas); p. ej. «Pasar a maniobra».</param>
    public ListaView(ListaEtiquetas<T> lista, string tituloFiltro, IReadOnlyList<ColumnaTabla> columnas, Action alCambiar,
                     Action<List<T>, string> alPonerSwcode, Func<string> busqueda, Func<string, Task> aviso,
                     (string Texto, Action<List<T>> Accion)? botonExtra = null)
    {
        _lista = lista; _alCambiar = alCambiar; _alPonerSwcode = alPonerSwcode; _busqueda = busqueda; _aviso = aviso;
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        Margin = new Thickness(0, 8, 0, 0);

        // ---- filtros (izquierda) ----
        var todas = new Button { Content = "Todas", MinWidth = 70 };
        var ninguna = new Button { Content = "Ninguna", MinWidth = 70 };
        todas.Click += (_, _) => Marcar(true);
        ninguna.Click += (_, _) => Marcar(false);
        var botones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        botones.Children.Add(todas); botones.Children.Add(ninguna);
        var izq = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Width = 210 };
        var titulo = new TextBlock { Text = tituloFiltro, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(titulo, 0);
        var scroll = new ScrollViewer { Content = _filtros, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(botones, 2);
        izq.Children.Add(titulo); izq.Children.Add(scroll); izq.Children.Add(botones);
        var marcoIzq = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#d0d5dc")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 0), Child = izq,
        };
        SetColumn(marcoIzq, 0);
        Children.Add(marcoIzq);

        // ---- derecha: SW-CODE + tabla ----
        Tabla = new TablaView(columnas);
        var der = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var sw = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        fila.Children.Add(new TextBlock { Text = "SW-CODE:", VerticalAlignment = VerticalAlignment.Center });
        fila.Children.Add(Swcode);
        var aplicarSel = new Button { Content = "Aplicar a seleccionadas" };
        var aplicarVis = new Button { Content = "Aplicar a todas las visibles" };
        fila.Children.Add(aplicarSel); fila.Children.Add(aplicarVis);
        fila.Children.Add(new TextBlock
        {
            Text = "(Ctrl+E selecciona todo lo visible)", Foreground = Dialogos.Gris, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        DockPanel.SetDock(fila, Dock.Left);
        sw.Children.Add(fila);
        if (botonExtra is { } extra)
        {
            var b = new Button { Content = extra.Texto };
            b.Click += (_, _) => extra.Accion(Tabla.Seleccion<T>());
            DockPanel.SetDock(b, Dock.Right);
            sw.Children.Add(b);
        }
        SetRow(sw, 0);
        der.Children.Add(sw);

        SetRow(Tabla, 1);
        der.Children.Add(Tabla);
        SetColumn(der, 1);
        Children.Add(der);

        Swcode.KeyDown += (_, e) => { if (e.Key == Key.Enter) PonerSwcode(soloSeleccion: true); };
        aplicarSel.Click += (_, _) => PonerSwcode(soloSeleccion: true);
        aplicarVis.Click += (_, _) => PonerSwcode(soloSeleccion: false);
    }

    private void Marcar(bool valor)
    {
        _lista.MarcarTodos(valor);
        Refrescar();
        _alCambiar();
    }

    /// <summary>Vuelve a dibujar las casillas de los filtros (tras cargar o quitar esquemas).</summary>
    public void Refrescar()
    {
        _refrescando = true;
        _filtros.Children.Clear();
        foreach (var (grupo, n) in _lista.Grupos())
        {
            var g = grupo;
            var cb = new CheckBox { Content = $"{g}  ({n})", IsChecked = _lista.GrupoIncluido(g) };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (_refrescando) return;
                _lista.MarcarGrupo(g, cb.IsChecked == true);
                _alCambiar();
            };
            _filtros.Children.Add(cb);
        }
        _refrescando = false;
    }

    /// <summary>Rellena la tabla con las filas que pasan la búsqueda.</summary>
    public void Pintar(string q)
    {
        var vistas = new List<FilaVista>();
        foreach (var f in _lista.Filas)
        {
            if (!_lista.Pasa(f, q)) continue;
            vistas.Add(new FilaVista
            {
                Vals = _lista.Valores(f), Pe = _lista.EsPe(f), Off = !_lista.GrupoIncluido(_lista.Clave(f)), Origen = f,
            });
        }
        Tabla.Poner(vistas);
    }

    /// <summary>Pone el SW-CODE escrito en las filas seleccionadas o en todas las visibles.</summary>
    private async void PonerSwcode(bool soloSeleccion)
    {
        string codigo = (Swcode.Text ?? "").Trim();
        List<T> filas;
        if (soloSeleccion)
        {
            filas = Tabla.Seleccion<T>();
            if (filas.Count == 0)
            {
                await _aviso("Selecciona primero las filas en la tabla (Mayús/Ctrl + clic, o Ctrl+E para todas las visibles).");
                return;
            }
        }
        else filas = Tabla.Visibles<T>();
        _alPonerSwcode(filas, codigo);
        _alCambiar();
        Tabla.Reseleccionar(filas);     // mantener la selección tras repintar
    }
}
