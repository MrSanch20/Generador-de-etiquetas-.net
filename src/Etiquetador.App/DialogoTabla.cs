using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Etiquetador.App;

/// <summary>Lo que el usuario ha elegido para un listado: fila de cabecera, columnas y tipo.</summary>
public sealed record ResultadoTabla(int? Cabecera, Dictionary<string, int?> Columnas, string Modo);

/// <summary>Pregunta qué contiene un listado Excel/CSV y en qué columnas está cada dato.</summary>
public sealed class DialogoTabla : Window
{
    private const string Ninguna = "(ninguna)";

    private readonly List<List<string>> _filas;
    private readonly AnalisisTabla _propuesta;
    private readonly RadioButton _bornas = new() { Content = "Bornas (regleta y borna)", GroupName = "modo" };
    private readonly RadioButton _elementos = new() { Content = "Elementos (KM100, Q100…)", GroupName = "modo" };
    private readonly NumericUpDown _filaCab = new() { Minimum = 0, Value = 0, Increment = 1, Width = 120, FormatString = "0" };
    private readonly Dictionary<string, ComboBox> _combos = new();
    private readonly TextBlock _aviso = new() { Foreground = new SolidColorBrush(Color.Parse("#b42318")) };
    private bool _actualizando;

    public DialogoTabla(string nombre, List<List<string>> filas, AnalisisTabla propuesta)
    {
        _filas = filas; _propuesta = propuesta;
        Title = $"Listado: {nombre}";
        Width = 760; Height = 600; MinWidth = 600; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _filaCab.Maximum = Math.Min(30, filas.Count);
        _filaCab.Value = propuesta.Cabecera != null ? propuesta.Cabecera.Value + 1 : 0;
        (propuesta.Modo == "bornas" ? _bornas : _elementos).IsChecked = true;

        var panel = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto") };
        int fila = 0;

        var titulo = new TextBlock { Text = "¿Qué contiene este listado?", FontWeight = FontWeight.Bold, FontSize = 15 };
        Grid.SetRow(titulo, fila++); panel.Children.Add(titulo);

        var modos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 6, 0, 0) };
        modos.Children.Add(_bornas); modos.Children.Add(_elementos);
        Grid.SetRow(modos, fila++); panel.Children.Add(modos);

        var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        cab.Children.Add(new TextBlock { Text = "Fila de cabecera (0 = no tiene):", VerticalAlignment = VerticalAlignment.Center });
        cab.Children.Add(_filaCab);
        Grid.SetRow(cab, fila++); panel.Children.Add(cab);

        var campos = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        var lista = new[] { ("regleta", "Regleta:"), ("borna", "Borna / marca:"), ("designacion", "Designación del elemento:"), ("descripcion", "Descripción (opcional):") };
        campos.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto");
        for (int i = 0; i < lista.Length; i++)
        {
            var (campo, texto) = lista[i];
            var etiqueta = new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2) };
            Grid.SetRow(etiqueta, i); Grid.SetColumn(etiqueta, 0);
            var cb = new ComboBox { Width = 300, Margin = new Thickness(0, 2) };
            Grid.SetRow(cb, i); Grid.SetColumn(cb, 1);
            _combos[campo] = cb;
            campos.Children.Add(etiqueta); campos.Children.Add(cb);
        }
        Grid.SetRow(campos, fila++); panel.Children.Add(campos);

        var vistaTitulo = new TextBlock { Text = "Primeras filas:", Margin = new Thickness(0, 10, 0, 2) };
        Grid.SetRow(vistaTitulo, fila++); panel.Children.Add(vistaTitulo);

        int nCol = filas[0].Count;
        var columnas = Enumerable.Range(0, nCol).Select(j => new ColumnaTabla(Letra(j), 90)).ToList();
        var tabla = new TablaView(columnas);
        tabla.Poner(filas.Take(12).Select(f => new FilaVista { Vals = f.ToArray() }).ToList());
        var vista = new ScrollViewer
        {
            Content = tabla, MinHeight = 160,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(vista, fila++); panel.Children.Add(vista);

        Grid.SetRow(_aviso, fila++); _aviso.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(_aviso);

        var cancelar = new Button { Content = "Cancelar", MinWidth = 90 };
        var cargar = new Button { Content = "Cargar", MinWidth = 90 };
        cancelar.Click += (_, _) => Close(null);
        cargar.Click += (_, _) => Aceptar();
        var botones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        botones.Children.Add(cargar); botones.Children.Add(cancelar);
        Grid.SetRow(botones, fila++); panel.Children.Add(botones);

        Content = panel;

        // selección inicial según la propuesta
        Actualizar(primeraVez: true);
        _bornas.IsCheckedChanged += (_, _) => Actualizar();
        _elementos.IsCheckedChanged += (_, _) => Actualizar();
        _filaCab.ValueChanged += (_, _) => Actualizar();
    }

    private static string Letra(int j)
    {
        string s = "";
        j++;
        while (j > 0)
        {
            int r = (j - 1) % 26;
            s = (char)('A' + r) + s;
            j = (j - 1) / 26;
        }
        return s;
    }

    private int Cab => (int)(_filaCab.Value ?? 0);

    private List<string> Opciones()
    {
        int cab = Cab;
        var nombres = cab > 0 && cab <= _filas.Count ? _filas[cab - 1] : Enumerable.Repeat("", _filas[0].Count).ToList();
        var ops = new List<string> { Ninguna };
        for (int j = 0; j < nombres.Count; j++) ops.Add($"{Letra(j)}  {nombres[j]}".TrimEnd());
        return ops;
    }

    private void Actualizar(bool primeraVez = false)
    {
        if (_actualizando) return;
        _actualizando = true;
        var ops = Opciones();
        foreach (var (campo, cb) in _combos)
        {
            int actual = primeraVez
                ? (_propuesta.Columnas.GetValueOrDefault(campo) is int c ? c + 1 : 0)
                : Math.Max(cb.SelectedIndex, 0);
            cb.ItemsSource = ops;
            cb.SelectedIndex = Math.Max(0, Math.Min(actual, ops.Count - 1));
        }
        bool bornas = _bornas.IsChecked == true;
        _combos["regleta"].IsEnabled = bornas;
        _combos["borna"].IsEnabled = bornas;
        _combos["designacion"].IsEnabled = !bornas;
        _actualizando = false;
    }

    private void Aceptar()
    {
        int cab = Cab;
        var columnas = _combos.ToDictionary(kv => kv.Key, kv => kv.Value.SelectedIndex > 0 ? (int?)(kv.Value.SelectedIndex - 1) : null);
        string modo = _bornas.IsChecked == true ? "bornas" : "elementos";
        if (modo == "bornas" && columnas["borna"] == null) { _aviso.Text = "Elige la columna de la borna / marca."; return; }
        if (modo == "elementos" && columnas["designacion"] == null) { _aviso.Text = "Elige la columna de la designación del elemento."; return; }
        Close(new ResultadoTabla(cab > 0 ? cab - 1 : null, columnas, modo));
    }

    public Task<ResultadoTabla?> Mostrar(Window dueno) => ShowDialog<ResultadoTabla?>(dueno);
}
