namespace Etiquetador.Core.Excel;

/// <summary>Guardado de las etiquetas en Excel, listo para importar en GeniusPro (Cembre). Reproduce excel.py.</summary>
public static class ExcelEtiquetas
{
    /// <summary>
    /// Fondo alterno por bloque (esquema + copia): se ve de un vistazo dónde empieza y acaba cada esquema y cada copia.
    /// La primera fila de cada bloque va en negrita.
    /// </summary>
    private static void Bandas(Hoja ws, int colEsquema, int colCopia)
    {
        int maxCol = ws.NumColumnas;
        if (maxCol <= Math.Max(colEsquema, colCopia)) return;     // hoja sin filas (p. ej. un esquema sin bornas)
        (string?, string?)? anterior = null;
        bool banda = false;
        for (int r = 0; r < ws.Filas.Count; r++)
        {
            var fila = ws.Filas[r];
            var bloque = (Texto(fila, colEsquema), Texto(fila, colCopia));
            if (anterior == null || bloque != anterior.Value)
            {
                banda = anterior != null ? !banda : false;
                anterior = bloque;
                ws.Estilo(r, colEsquema, EstiloCelda.Negrita);
                ws.Estilo(r, colCopia, EstiloCelda.Negrita);
            }
            if (banda)
            {
                for (int c = 0; c < maxCol; c++)
                {
                    bool negrita = ws.Estilos.TryGetValue((r, c), out var e) && e == EstiloCelda.Negrita;
                    ws.Estilo(r, c, negrita ? EstiloCelda.NegritaBanda : EstiloCelda.Banda);
                }
            }
        }
    }

    private static string? Texto(List<object?> fila, int col) =>
        col < fila.Count ? Convert.ToString(fila[col], System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Solo ancho de columnas, SIN fila de cabecera: los datos empiezan en la fila 1 (así GeniusPro importa desde la primera línea).</summary>
    private static void Anchos(Hoja ws, params double[] anchos) => ws.Anchos.AddRange(anchos);

    private static void Cabecera(Hoja ws, string[] cols, double[] anchos)
    {
        ws.Agregar(cols.Cast<object?>().ToArray());
        for (int i = 0; i < cols.Length; i++) ws.Estilo(0, i, EstiloCelda.Cabecera);
        ws.Anchos.AddRange(anchos);
        ws.CongelarCabecera = true;
    }

    private static void HojaDetalleBornas(LibroXlsx wb, IEnumerable<Borna> filas, string titulo = "Bornas")
    {
        var ws = wb.CrearHoja(titulo);
        // columnas: A Esquema | B Copia | C Marca (etiqueta) | D SW-CODE | E Regleta | F Pos. | G Descripción | H Página
        Anchos(ws, 22, 7, 12, 11, 12, 7, 40, 11);
        foreach (var f in filas)
            ws.Agregar(f.Esquema, f.Copia, f.Marca, f.SwCode, f.Regleta, f.Pos, f.Descripcion, f.Pagina);
        Bandas(ws, 0, 1);
    }

    private static void HojaDetalleElementos(LibroXlsx wb, IEnumerable<Elemento> elementos, string titulo = "Elementos")
    {
        var ws = wb.CrearHoja(titulo);
        // columnas: A Esquema | B Copia | C Designación (etiqueta) | D SW-CODE | E Tipo | F Páginas/filas
        Anchos(ws, 22, 7, 14, 11, 28, 18);
        foreach (var e in elementos)
            ws.Agregar(e.Esquema, e.Copia, e.Designacion, e.SwCode, e.Tipo, e.PaginasTexto);
        Bandas(ws, 0, 1);
    }

    private static void HojaDetalleCables(LibroXlsx wb, IEnumerable<Cable> cables, string titulo)
    {
        var ws = wb.CrearHoja(titulo);
        // columnas: A Esquema | B Copia | C Nº de hilo (etiqueta) | D SW-CODE | E Color | F Página | G Extremo
        Anchos(ws, 22, 7, 14, 11, 9, 8, 10);
        foreach (var c in cables)
            ws.Agregar(c.Esquema, c.Copia, c.Numero, c.SwCode, c.Color, c.Pagina, c.Extremo);
        Bandas(ws, 0, 1);
    }

    /// <summary>Una etiqueta de cable por fila: solo el número de hilo (columna C) y el SW-CODE.</summary>
    private static void HojaGeniusProCables(LibroXlsx wb, IEnumerable<Cable> cables, string titulo)
    {
        var ws = wb.CrearHoja(titulo);
        // columnas: A Esquema | B Copia | C Texto (nº de hilo) | D SW-CODE (cada hilo sale dos veces seguidas: una etiqueta para cada punta)
        Anchos(ws, 22, 7, 14, 11);
        foreach (var c in cables)
            ws.Agregar(c.Esquema, c.Copia, c.Numero, c.SwCode);
        Bandas(ws, 0, 1);
    }

    /// <summary>Una etiqueta por fila, lista para importar: texto y SW-CODE del consumible.</summary>
    private static void HojaGeniusPro(LibroXlsx wb, IEnumerable<(string Texto, string SwCode, string Esquema, string Copia)> filas, string titulo = "GeniusPro")
    {
        var ws = wb.CrearHoja(titulo);
        // columnas: A Esquema | B Copia | C Texto (etiqueta) | D SW-CODE
        // (la etiqueta va en la columna C en TODAS las hojas: en GeniusPro, Valor inicial = [C])
        Anchos(ws, 22, 7, 14, 11);
        foreach (var (texto, swcode, esquema, copia) in filas)
            ws.Agregar(esquema, copia, texto, swcode);
        Bandas(ws, 0, 1);
    }

    private static void HojaResumen(LibroXlsx wb, IEnumerable<string> grupos, string tituloCol, string tituloN,
                                    IEnumerable<string>? swcodes = null, string titulo = "Resumen")
    {
        var ws = wb.CrearHoja(titulo);
        Cabecera(ws, new[] { tituloCol, tituloN }, new double[] { 28, 13 });
        var cuenta = new MapaOrdenado<string, int>();
        foreach (var g in grupos) cuenta[g] = (cuenta.Contiene(g) ? cuenta[g] : 0) + 1;
        foreach (var (g, n) in cuenta.Pares()) ws.Agregar(g, n);
        ws.Agregar("TOTAL", $"=SUM(B2:B{cuenta.Count + 1})");
        int total = ws.Filas.Count - 1;
        for (int c = 0; c < 2; c++) ws.Estilo(total, c, EstiloCelda.Negrita);
        // Etiquetas por consumible (SW-CODE): cuántas hay que imprimir en cada uno
        var sw = swcodes?.ToList() ?? new List<string>();
        if (sw.Count > 0)
        {
            ws.Agregar();
            ws.Agregar("SW-CODE", "Nº etiquetas");
            int cab = ws.Filas.Count - 1;
            for (int c = 0; c < 2; c++) ws.Estilo(cab, c, EstiloCelda.Cabecera);
            var porCodigo = new MapaOrdenado<string, int>();
            foreach (var c in sw)
            {
                var k = string.IsNullOrEmpty(c) ? "(sin SW-CODE)" : c;
                porCodigo[k] = (porCodigo.Contiene(k) ? porCodigo[k] : 0) + 1;
            }
            foreach (var (c, n) in porCodigo.Pares()) ws.Agregar(c, n);
        }
    }

    private static IEnumerable<(string, string, string, string)> Tuplas(IEnumerable<Borna> f) =>
        f.Select(x => (x.Marca, x.SwCode, x.Esquema, x.Copia));
    private static IEnumerable<(string, string, string, string)> Tuplas(IEnumerable<Elemento> f) =>
        f.Select(x => (x.Designacion, x.SwCode, x.Esquema, x.Copia));

    /// <summary>Solo bornas: detalle, columna para GeniusPro y resumen por regleta.</summary>
    public static void GuardarBornas(IEnumerable<Borna> filasEntrada, string salida)
    {
        var filas = filasEntrada.ToList();
        var wb = new LibroXlsx();
        HojaGeniusPro(wb, Tuplas(filas));
        HojaDetalleBornas(wb, filas);
        HojaResumen(wb, filas.Select(f => f.Regleta), "Regleta", "Nº bornas", filas.Select(f => f.SwCode));
        wb.Guardar(salida);
    }

    /// <summary>Solo elementos (Q, KM, K, F…): detalle, columna para GeniusPro y resumen por tipo.</summary>
    public static void GuardarElementos(IEnumerable<Elemento> elementosEntrada, string salida)
    {
        var elementos = elementosEntrada.ToList();
        var wb = new LibroXlsx();
        HojaGeniusPro(wb, Tuplas(elementos));
        HojaDetalleElementos(wb, elementos);
        HojaResumen(wb, elementos.Select(e => e.Tipo), "Tipo", "Nº elementos", elementos.Select(e => e.SwCode));
        wb.Guardar(salida);
    }

    /// <summary>Cables de fuerza o de maniobra: detalle, hoja para GeniusPro y resumen por color de hilo.</summary>
    public static void GuardarCables(IEnumerable<Cable> cablesEntrada, string salida, string clase)
    {
        var cables = cablesEntrada.ToList();
        var wb = new LibroXlsx();
        HojaGeniusProCables(wb, cables, "GeniusPro");
        HojaDetalleCables(wb, cables, $"Cables {clase}");
        HojaResumen(wb, cables.Select(c => c.Color), "Color de hilo", "Nº cables", cables.Select(c => c.SwCode));
        wb.Guardar(salida);
    }

    /// <summary>Bornas, elementos y cables en el mismo libro, cada uno con su hoja para GeniusPro.</summary>
    public static void GuardarTodo(IEnumerable<Borna> bornasEntrada, IEnumerable<Elemento> elementosEntrada, string salida,
                                   IEnumerable<Cable>? fuerzaEntrada = null, IEnumerable<Cable>? maniobraEntrada = null)
    {
        var filas = bornasEntrada.ToList();
        var elementos = elementosEntrada.ToList();
        var fuerza = fuerzaEntrada?.ToList() ?? new List<Cable>();
        var maniobra = maniobraEntrada?.ToList() ?? new List<Cable>();
        var wb = new LibroXlsx();
        HojaGeniusPro(wb, Tuplas(filas), "GeniusPro bornas");
        HojaGeniusPro(wb, Tuplas(elementos), "GeniusPro elementos");
        if (fuerza.Count > 0) HojaGeniusProCables(wb, fuerza, "GeniusPro cables fuerza");
        if (maniobra.Count > 0) HojaGeniusProCables(wb, maniobra, "GeniusPro cables maniobra");
        HojaDetalleBornas(wb, filas);
        HojaDetalleElementos(wb, elementos);
        if (fuerza.Count > 0) HojaDetalleCables(wb, fuerza, "Cables fuerza");
        if (maniobra.Count > 0) HojaDetalleCables(wb, maniobra, "Cables maniobra");
        var grupos = Enumerable.Repeat("Bornas", filas.Count)
            .Concat(Enumerable.Repeat("Elementos", elementos.Count))
            .Concat(Enumerable.Repeat("Cables fuerza", fuerza.Count))
            .Concat(Enumerable.Repeat("Cables maniobra", maniobra.Count));
        var sw = filas.Select(f => f.SwCode).Concat(elementos.Select(e => e.SwCode))
            .Concat(fuerza.Select(c => c.SwCode)).Concat(maniobra.Select(c => c.SwCode));
        HojaResumen(wb, grupos, "Etiquetas", "Cantidad", sw);
        wb.Guardar(salida);
    }
}
