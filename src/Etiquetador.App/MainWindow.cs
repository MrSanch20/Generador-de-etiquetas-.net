using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Etiquetador.Core;

namespace Etiquetador.App;

public sealed class MainWindow : Window
{
    private const string AppName = "Etiquetador de cuadros";

    private readonly Sesion _s = new();
    private readonly ListaView<Borna> _bornas;
    private readonly ListaView<Elemento> _elementos;
    private readonly ListaView<Cable> _fuerza;
    private readonly ListaView<Cable> _maniobra;
    private readonly TablaView _todo;
    private readonly TabControl _pestanas = new();
    private readonly TextBox _busqueda = new() { Width = 260 };
    private readonly TextBlock _lblPdf = new();
    private readonly TextBlock _lblTags = new() { FontSize = 16, FontWeight = FontWeight.Bold };
    private readonly TextBlock _lblEstado = new() { Foreground = Dialogos.Gris, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _panelCopias = new();
    private readonly Button _btnAbrir = new() { Content = "Abrir esquema…" };
    private readonly Button _btnAnadir = new() { Content = "Añadir esquema…" };
    private readonly Button _btnLimpiar = new() { Content = "Limpiar todo" };
    private readonly Button _btnExcel = new() { Content = "Guardar Excel…", IsEnabled = false, MinWidth = 220 };
    private readonly Button _btnCopiar = new() { Content = "Copiar esta lista", IsEnabled = false };
    private bool _cargando;

    public MainWindow(string[] args)
    {
        Title = AppName;
        Background = Dialogos.Fondo;
        Width = 1100; Height = 700; MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ---- pestañas ----
        _bornas = new ListaView<Borna>(_s.Bornas, "Regletas a incluir", new[]
        {
            new ColumnaTabla("Esquema", 150), new ColumnaTabla("Regleta", 80), new ColumnaTabla("Pos.", 50, true),
            new ColumnaTabla("Marca", 100), new ColumnaTabla("Descripción (orientativa)", 320, Estirar: true),
            new ColumnaTabla("Pág. / fila", 80, true), new ColumnaTabla("SW-CODE", 80),
        }, Pintar, (filas, codigo) => _s.PonerSwcodeBornas(filas, codigo), () => Busqueda(), Aviso);

        _elementos = new ListaView<Elemento>(_s.Elementos, "Tipos a incluir", new[]
        {
            new ColumnaTabla("Esquema", 150), new ColumnaTabla("Designación", 110), new ColumnaTabla("Tipo", 220),
            new ColumnaTabla("Páginas / filas", 200, Estirar: true), new ColumnaTabla("SW-CODE", 80),
        }, Pintar, (filas, codigo) => _s.PonerSwcodeElementos(filas, codigo), () => Busqueda(), Aviso);

        var colsCable = new[]
        {
            new ColumnaTabla("Esquema", 150), new ColumnaTabla("Nº hilo", 120), new ColumnaTabla("Color", 80),
            new ColumnaTabla("Pág.", 60, true, Estirar: true), new ColumnaTabla("SW-CODE", 80),
        };
        _fuerza = new ListaView<Cable>(_s.Fuerza, "Colores a incluir", colsCable, Pintar,
            (filas, codigo) => _s.PonerSwcodeCables("fuerza", filas, codigo), () => Busqueda(), Aviso,
            ("Pasar a maniobra", filas => MoverCables(filas, "fuerza")));
        _maniobra = new ListaView<Cable>(_s.Maniobra, "Colores a incluir", colsCable, Pintar,
            (filas, codigo) => _s.PonerSwcodeCables("maniobra", filas, codigo), () => Busqueda(), Aviso,
            ("Pasar a fuerza", filas => MoverCables(filas, "maniobra")));

        // Pestaña "Todo": bornas, elementos y cables juntos (usa los filtros de las otras)
        _todo = new TablaView(new[]
        {
            new ColumnaTabla("Esquema", 150), new ColumnaTabla("Etiqueta", 100), new ColumnaTabla("Texto", 110),
            new ColumnaTabla("Regleta / tipo", 190), new ColumnaTabla("Descripción", 260, Estirar: true),
            new ColumnaTabla("Pág.", 90), new ColumnaTabla("SW-CODE", 80),
        });
        var todoPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        var todoNota = new TextBlock { Text = "Bornas, elementos y cables juntos. Usa los filtros de las otras pestañas.", Foreground = Dialogos.Gris, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(todoNota, 0); Grid.SetRow(_todo, 1);
        todoPanel.Children.Add(todoNota); todoPanel.Children.Add(_todo);

        _pestanas.Items.Add(new TabItem { Header = "Bornas", Content = _bornas });
        _pestanas.Items.Add(new TabItem { Header = "Elementos", Content = _elementos });
        _pestanas.Items.Add(new TabItem { Header = "Cables fuerza", Content = _fuerza });
        _pestanas.Items.Add(new TabItem { Header = "Cables maniobra", Content = _maniobra });
        _pestanas.Items.Add(new TabItem { Header = "Todo", Content = todoPanel });
        _pestanas.SelectionChanged += (_, e) => { if (e.Source == _pestanas) ActualizarBotones(); };

        // ---- cuerpo ----
        var raiz = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto"), Margin = new Thickness(14, 12, 14, 12) };

        // fila 0: título y botones
        var top = new DockPanel { LastChildFill = false };
        var titulo = new TextBlock { Text = AppName, FontSize = 22, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(titulo, Dock.Left);
        var botonesTop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnFormatos = new Button { Content = "Formatos admitidos" };
        botonesTop.Children.Add(btnFormatos); botonesTop.Children.Add(_btnAbrir); botonesTop.Children.Add(_btnAnadir); botonesTop.Children.Add(_btnLimpiar);
        DockPanel.SetDock(botonesTop, Dock.Right);
        top.Children.Add(titulo); top.Children.Add(botonesTop);
        Fila(raiz, top, 0);

        Fila(raiz, new TextBlock
        {
            Text = "Formatos admitidos:  " + string.Join("  ", LectorArchivos.ExtensionesAdmitidas()),
            Foreground = Dialogos.Gris, Margin = new Thickness(0, 6, 0, 4),
        }, 1);

        var info = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        DockPanel.SetDock(_lblPdf, Dock.Left); DockPanel.SetDock(_lblTags, Dock.Right);
        info.Children.Add(_lblPdf); info.Children.Add(_lblTags);
        Fila(raiz, info, 2);

        _panelCopias.Margin = new Thickness(0, 0, 0, 6);
        Fila(raiz, _panelCopias, 3);

        var barra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        barra.Children.Add(new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center });
        barra.Children.Add(_busqueda);
        Fila(raiz, barra, 4);

        Fila(raiz, _pestanas, 5);

        var bot = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var botonesBot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        botonesBot.Children.Add(_btnCopiar); botonesBot.Children.Add(_btnExcel);
        DockPanel.SetDock(botonesBot, Dock.Right);
        _lblEstado.Text = "Bornas: «Plano de bornes». Elementos: los de la placa. Cables: el número de cada hilo del esquema.";
        _lblEstado.MaxWidth = 700;
        DockPanel.SetDock(_lblEstado, Dock.Left);
        _lblEstado.VerticalAlignment = VerticalAlignment.Center;
        bot.Children.Add(_lblEstado); bot.Children.Add(botonesBot);
        Fila(raiz, bot, 6);

        Content = raiz;

        // ---- eventos ----
        btnFormatos.Click += async (_, _) => await MostrarFormatos();
        _btnAbrir.Click += async (_, _) => await ElegirArchivo(anadir: false);
        _btnAnadir.Click += async (_, _) => await ElegirArchivo(anadir: true);
        _btnLimpiar.Click += async (_, _) => await LimpiarTodo();
        _btnExcel.Click += async (_, _) => await Guardar();
        _btnCopiar.Click += async (_, _) => await Copiar();
        _busqueda.TextChanged += (_, _) => Pintar();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (object? _, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, async (object? _, DragEventArgs e) =>
        {
            var rutas = e.Data.GetFiles()?.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
            if (rutas is { Count: > 0 }) await Abrir(rutas, anadir: false);
        });

        RefrescarTodo();
        Opened += async (_, _) =>
        {
            if (args.Length > 0 && args[0] == "--demo") CargarDemo();
            else if (args.Length > 0) await Abrir(args.ToList(), anadir: false);
        };
    }

    private static void Fila(Grid g, Control c, int fila)
    {
        Grid.SetRow(c, fila);
        g.Children.Add(c);
    }

    private string Busqueda() => (_busqueda.Text ?? "").Trim().ToLowerInvariant();

    private void Estado(string texto) => _lblEstado.Text = texto;

    private Task Aviso(string texto) => Dialogos.Mensaje(this, AppName, texto);

    // ---------------------------------------------------------------------
    // Acciones
    // ---------------------------------------------------------------------
    private async Task MostrarFormatos()
    {
        string texto = string.Join("\n\n", LectorArchivos.Formatos.Select(f => $"{f.Nombre}  ({string.Join("  ", f.Extensiones)})\n{f.Nota}"));
        texto += "\n\nOCR en este programa: " + (LectorOcr.Disponible
            ? "SÍ incluido."
            : "NO incluido (no lee planos escaneados ni imágenes). Usa PDF con texto, DXF o listados Excel/CSV.");
        await Dialogos.Mensaje(this, "Formatos admitidos", texto);
    }

    private async Task ElegirArchivo(bool anadir)
    {
        var tipos = new List<FilePickerFileType>
        {
            new("Esquemas y listados") { Patterns = LectorArchivos.ExtensionesAdmitidas().Select(e => "*" + e).ToList() },
            new("PDF") { Patterns = new[] { "*.pdf" } },
            new("AutoCAD (DXF, DWG)") { Patterns = new[] { "*.dxf", "*.dwg" } },
            new("Imagen escaneada") { Patterns = LectorArchivos.ExtImagen.Select(e => "*" + e).ToList() },
            new("Listado Excel / CSV") { Patterns = new[] { "*.xlsx", "*.xlsm", "*.csv", "*.txt" } },
            FilePickerFileTypes.All,
        };
        var archivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = anadir ? "Añadir esquemas" : "Elegir esquemas (puedes elegir varios)",
            AllowMultiple = true,
            FileTypeFilter = tipos,
        });
        var rutas = archivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
        if (rutas.Count > 0) await Abrir(rutas, anadir);
    }

    /// <summary>Abre uno o varios archivos. anadir = false empieza de cero; true los suma a los cargados.</summary>
    private async Task Abrir(List<string> rutasEntrada, bool anadir)
    {
        if (_cargando) return;
        var rutas = _s.Preparar(rutasEntrada, anadir, out var malos, out var repetidos);
        if (malos.Count > 0)
            await Aviso("Formato no reconocido: " + string.Join(", ", malos.Select(Path.GetFileName))
                        + "\n\nExtensiones admitidas:\n" + string.Join("  ", LectorArchivos.ExtensionesAdmitidas())
                        + "\n\nPulsa «Formatos admitidos» para ver qué hace cada una.");
        if (repetidos.Count > 0)
            await Aviso("Ya estaban cargados (no se añaden otra vez):\n" + string.Join("\n", repetidos.Select(Path.GetFileName)));
        RefrescarTodo();
        if (rutas.Count == 0) return;
        var tablas = rutas.Where(r => LectorArchivos.TipoArchivo(r) == "tabla").ToList();
        var esquemas = rutas.Where(r => LectorArchivos.TipoArchivo(r) != "tabla").ToList();
        if (esquemas.Count == 0)
        {
            foreach (var t in tablas) await AbrirTabla(t);
            return;
        }

        Bloquear(true);
        List<ResultadoArchivo> resultados;
        try
        {
            resultados = await Task.Run(() =>
            {
                var lista = new List<ResultadoArchivo>();
                for (int i = 0; i < esquemas.Count; i++)
                {
                    string prefijo = esquemas.Count > 1 ? $"Esquema {i + 1} de {esquemas.Count} ({Path.GetFileName(esquemas[i])}): " : "";
                    lista.Add(CargadorEsquemas.Leer(esquemas[i], m => Dispatcher.UIThread.Post(() => Estado(prefijo + m))));
                }
                return lista;
            });
        }
        finally { Bloquear(false); }

        var res = _s.Incorporar(resultados);
        if (res.HayFilasNuevas)
        {
            _elementos.Swcode.Text = _s.SwcodeElementos;
            foreach (var (clase, vista) in new[] { ("fuerza", _fuerza), ("maniobra", _maniobra) })
                if (_s.SwcodeCables(clase).Length > 0) vista.Swcode.Text = _s.SwcodeCables(clase);
            _busqueda.Text = "";
            _pestanas.SelectedIndex = _s.Bornas.Filas.Count > 0 ? 0 : _s.Elementos.Filas.Count > 0 ? 1
                : _s.Fuerza.Filas.Count > 0 ? 2 : 3;
        }
        RefrescarTodo();
        if (res.Estado != null) Estado(res.Estado);
        if (res.Errores.Count > 0) await Aviso(string.Join("\n\n", res.Errores));
        if (res.AvisosSinOcr.Count > 0 && !res.HayFilasNuevas)
            await Aviso("Estas páginas no tienen texto (son imagen o dibujo) y no se han podido leer porque esta versión " +
                        "no incluye el reconocimiento de texto (OCR):\n" + string.Join("\n", res.AvisosSinOcr) + "\n\n" +
                        "El resto de páginas sí se han leído.");
        if (res.AvisosOcr.Count > 0)
            await Aviso("Algunas páginas eran imagen (escaneadas) y se han leído con reconocimiento de texto (OCR):\n" +
                        string.Join("\n", res.AvisosOcr) + "\n\nEl OCR puede equivocarse (por ejemplo Q100H en vez de Q100). " +
                        "Revisa la lista antes de imprimir.");
        foreach (var t in tablas) await AbrirTabla(t);
    }

    private async Task AbrirTabla(string ruta)
    {
        List<List<string>> filas;
        try { filas = LectorTabla.Leer(ruta); }
        catch (ErrorLectura e)
        {
            Estado("No se ha podido leer el esquema.");
            await Aviso($"{Path.GetFileName(ruta)}:\n{e.Message}");
            return;
        }
        var propuesta = ImportadorTabla.Analizar(filas.Select(f => (IReadOnlyList<string>)f).ToList());
        var dlg = new DialogoTabla(Path.GetFileName(ruta), filas, propuesta);
        var r = await dlg.Mostrar(this);
        if (r == null) return;
        try
        {
            string estado = _s.IncorporarTabla(ruta, filas.Select(f => (IReadOnlyList<string>)f).ToList(), r.Cabecera, r.Columnas, r.Modo);
            _pestanas.SelectedIndex = r.Modo == "bornas" ? 0 : 1;
            if (r.Modo != "bornas") _elementos.Swcode.Text = _s.SwcodeElementos;
            RefrescarTodo();
            Estado(estado);
        }
        catch (ErrorLectura e)
        {
            Estado("No se ha podido leer el esquema.");
            await Aviso(e.Message);
        }
    }

    private void Bloquear(bool si)
    {
        _cargando = si;
        _btnAbrir.IsEnabled = !si; _btnAnadir.IsEnabled = !si; _btnLimpiar.IsEnabled = !si;
    }

    private async Task LimpiarTodo()
    {
        if (_s.Esquemas().Count == 0) return;
        if (!await Dialogos.Preguntar(this, AppName, "¿Quitar todos los esquemas y empezar de cero?")) return;
        _s.Quitar(_s.Esquemas().ToHashSet());
        RefrescarTodo();
        Estado("Lista vacía. Abre o añade esquemas.");
    }

    private async Task QuitarEsquema(string esquema)
    {
        if (!await Dialogos.Preguntar(this, AppName, $"¿Quitar el esquema {esquema} de la lista?")) return;
        _s.Quitar(new HashSet<string> { esquema });
        RefrescarTodo();
        Estado($"Esquema {esquema} quitado.");
    }

    private async void MoverCables(List<Cable> filas, string desde)
    {
        if (filas.Count == 0)
        {
            await Aviso("Selecciona primero los cables en la tabla (Mayús/Ctrl + clic, o Ctrl+E para todos los visibles).");
            return;
        }
        int n = _s.MoverCables(filas, desde);
        RefrescarTodo();
        Estado($"{n} cables pasados a {(desde == "fuerza" ? "maniobra" : "fuerza")}.");
    }

    private void CargarDemo()
    {
        Demo.Cargar(_s);
        _elementos.Swcode.Text = _s.SwcodeElementos;
        _s.Copias[_s.Esquemas()[0]] = 2;
        RefrescarTodo();
        Estado("Datos de ejemplo inventados (modo demostración).");
    }

    // ---------------------------------------------------------------------
    // Pintar
    // ---------------------------------------------------------------------
    /// <summary>Redibuja filtros, copias, cabecera y tablas (tras cargar o quitar esquemas).</summary>
    private void RefrescarTodo()
    {
        _bornas.Refrescar(); _elementos.Refrescar(); _fuerza.Refrescar(); _maniobra.Refrescar();
        _lblPdf.Text = _s.Cabecera();
        PintarCopias();
        Pintar();
    }

    /// <summary>Una casilla «Copias» por esquema. Se conserva lo que ya se había puesto.</summary>
    private void PintarCopias()
    {
        _panelCopias.Children.Clear();
        var esquemas = _s.Esquemas();
        if (esquemas.Count == 0) return;
        _panelCopias.Children.Add(new TextBlock { Text = "Copias:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var esq in esquemas)
        {
            var e = esq;
            var caja = new NumericUpDown
            {
                Minimum = 1, Maximum = 99, Increment = 1, FormatString = "0", Width = 110,
                Value = _s.Copias.GetValueOrDefault(e, 1),
            };
            caja.ValueChanged += (_, args) =>
            {
                _s.Copias[e] = (int)Math.Max(1, args.NewValue ?? 1);
                Pintar();
            };
            var quitar = new Button { Content = "✕", Margin = new Thickness(4, 0, 0, 0) };
            quitar.Click += async (_, _) => await QuitarEsquema(e);
            var celda = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 1, 18, 1) };
            celda.Children.Add(new TextBlock { Text = e, VerticalAlignment = VerticalAlignment.Center });
            celda.Children.Add(caja); celda.Children.Add(quitar);
            _panelCopias.Children.Add(celda);
        }
        _panelCopias.Children.Add(new TextBlock { Text = "(✕ quita el esquema)", Foreground = Dialogos.Gris, VerticalAlignment = VerticalAlignment.Center });
    }

    private Que QueActual => _pestanas.SelectedIndex switch
    {
        0 => Que.Bornas, 1 => Que.Elementos, 2 => Que.CablesFuerza, 3 => Que.CablesManiobra, _ => Que.Todo,
    };

    private void Pintar()
    {
        string q = Busqueda();
        _bornas.Pintar(q);
        _elementos.Pintar(q);
        _fuerza.Pintar(q); _maniobra.Pintar(q);

        // pestaña Todo
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
        _todo.Poner(filas);

        _lblTags.Text = _s.ResumenEtiquetas();
        ActualizarBotones();
    }

    /// <summary>Texto y estado de los botones de abajo según la pestaña activa (no toca las tablas).</summary>
    private void ActualizarBotones()
    {
        var que = QueActual;
        int n = _s.Cuenta(que);
        _btnExcel.Content = $"Guardar Excel de {Sesion.NombreQue(que)}…";
        _btnExcel.IsEnabled = n > 0;
        _btnCopiar.IsEnabled = n > 0;
    }

    // ---------------------------------------------------------------------
    // Guardar / copiar
    // ---------------------------------------------------------------------
    private async Task Guardar()
    {
        var que = QueActual;
        string nombreQue = Sesion.NombreQue(que);
        IStorageFolder? carpeta = null;
        try
        {
            if (_s.RutaActual != null)
                carpeta = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_s.RutaActual) ?? "");
        }
        catch (Exception) { }
        var archivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Guardar Excel de {nombreQue}",
            DefaultExtension = "xlsx",
            SuggestedFileName = _s.NombreExcelPropuesto(que),
            SuggestedStartLocation = carpeta,
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } },
        });
        var ruta = archivo?.TryGetLocalPath();
        if (ruta == null) return;
        try
        {
            _s.GuardarExcel(que, ruta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Aviso("No se puede guardar: ese Excel está abierto. Ciérralo y vuelve a intentarlo.");
            return;
        }
        Estado($"Excel de {nombreQue} guardado: {Path.GetFileName(ruta)} ({_s.Textos(que).Count} etiquetas)");
        if (OperatingSystem.IsWindows() && await Dialogos.Preguntar(this, AppName, $"Excel de {nombreQue} guardado.\n¿Quieres abrirlo ahora?"))
        {
            try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }

    private async Task Copiar()
    {
        var que = QueActual;
        var textos = _s.Textos(que);
        if (Clipboard != null) await Clipboard.SetTextAsync(string.Join("\n", textos));
        Estado($"Copiadas {textos.Count} etiquetas ({Sesion.NombreQue(que)}), una por línea. Pégalas en Excel o en GeniusPro.");
    }
}
