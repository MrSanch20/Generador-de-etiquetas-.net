namespace Etiquetador.Core;

/// <summary>
/// Una lista de etiquetas con sus filtros por grupo (regleta, tipo, color…). Es la lógica de cada pestaña
/// de la ventana, sin nada de interfaz.
/// </summary>
public sealed class ListaEtiquetas<T> where T : class, IFila
{
    public List<T> Filas { get; private set; } = new();
    /// <summary>Grupo -> ¿incluido? (en orden de aparición).</summary>
    public MapaOrdenado<string, bool> Incluir { get; set; } = new();
    public Func<T, string> Clave { get; set; } = _ => "";
    public Func<T, string[]> Valores { get; set; } = _ => Array.Empty<string>();
    public Func<T, string> Texto { get; set; } = _ => "";
    public Func<T, bool> EsPe { get; set; } = _ => false;

    /// <summary>Carga las filas, respetando las casillas de grupo que ya estaban tocadas.</summary>
    public void Cargar(IEnumerable<T> filas, ISet<string>? activos = null)
    {
        Filas = filas.ToList();
        var antes = Incluir.Pares().ToDictionary(p => p.Key, p => p.Value);
        Incluir = new MapaOrdenado<string, bool>();
        var cuenta = new MapaOrdenado<string, int>();
        foreach (var f in Filas)
        {
            var k = Clave(f);
            cuenta[k] = (cuenta.Contiene(k) ? cuenta[k] : 0) + 1;
        }
        foreach (var (grupo, _) in cuenta.Pares())
            Incluir[grupo] = antes.TryGetValue(grupo, out var v) ? v : (activos == null || activos.Contains(grupo));
    }

    public IReadOnlyList<KeyValuePair<string, int>> Grupos()
    {
        var cuenta = new MapaOrdenado<string, int>();
        foreach (var f in Filas)
        {
            var k = Clave(f);
            cuenta[k] = (cuenta.Contiene(k) ? cuenta[k] : 0) + 1;
        }
        return cuenta.Pares().ToList();
    }

    public bool GrupoIncluido(string grupo) => Incluir.TryGetValue(grupo, out var v) && v;
    public void MarcarGrupo(string grupo, bool valor) { if (Incluir.Contiene(grupo)) Incluir[grupo] = valor; }
    public void MarcarTodos(bool valor) { foreach (var k in Incluir.Claves.ToList()) Incluir[k] = valor; }

    public List<T> Incluidas() => Filas.Where(f => GrupoIncluido(Clave(f))).ToList();

    /// <summary>¿La fila pasa el texto de búsqueda (en minúsculas)? Sin texto, todas.</summary>
    public bool Pasa(T f, string q) =>
        q.Length == 0 || Valores(f).Any(v => v.ToLowerInvariant().Contains(q, StringComparison.Ordinal));

    /// <summary>Pone el SW-CODE en las filas dadas.</summary>
    public static void PonerSwcode(IEnumerable<T> filas, string codigo)
    {
        foreach (var f in filas) f.SwCode = codigo.Trim();
    }
}

/// <summary>Lo que se ha leído de un archivo (para pasarlo luego a la sesión).</summary>
public sealed class ResultadoArchivo
{
    public string Ruta { get; init; } = "";
    public string Nombre { get; init; } = "";
    public List<Borna> Bornas { get; init; } = new();
    public List<Elemento> Elementos { get; init; } = new();
    public List<Cable> Cables { get; init; } = new();
    public List<int> Ocr { get; init; } = new();
    public List<int> SinOcr { get; init; } = new();
    public string? Error { get; init; }
}

/// <summary>Resumen de una carga para enseñárselo al usuario.</summary>
public sealed class ResumenCarga
{
    public List<string> Errores { get; } = new();
    public List<string> AvisosOcr { get; } = new();
    public List<string> AvisosSinOcr { get; } = new();
    public List<string> Resumen { get; } = new();
    public int SinNumero { get; set; }
    public int PaginasSinOcr { get; set; }
    public bool HayFilasNuevas { get; set; }
    /// <summary>Texto para la barra de estado (null si no hay nada que decir).</summary>
    public string? Estado { get; set; }
}

public enum Que { Bornas, Elementos, CablesFuerza, CablesManiobra, Todo }

/// <summary>
/// Todo el estado y la lógica del programa, sin interfaz: lo cargado, los filtros, el SW-CODE, las copias por esquema
/// y el guardado en Excel. La ventana (Avalonia) solo pinta esto y llama a sus métodos.
/// </summary>
public sealed class Sesion
{
    public Ajustes Cfg { get; }
    public ListaEtiquetas<Borna> Bornas { get; } = new();
    public ListaEtiquetas<Elemento> Elementos { get; } = new();
    public ListaEtiquetas<Cable> Fuerza { get; } = new();
    public ListaEtiquetas<Cable> Maniobra { get; } = new();
    /// <summary>esquema -> nº de copias.</summary>
    public Dictionary<string, int> Copias { get; } = new();
    /// <summary>Carpeta del último archivo abierto (para proponer dónde guardar).</summary>
    public string? RutaActual { get; private set; }

    public Sesion(Ajustes? cfg = null)
    {
        Cfg = cfg ?? Config.Cargar();

        // con varios esquemas, la misma regleta (XALI) de cada esquema se filtra por separado
        Bornas.Clave = f => Varios ? $"{f.Regleta} · {f.Esquema}" : f.Regleta;
        Bornas.Valores = f => new[] { f.Esquema, f.Regleta, f.Pos.ToString(), f.Marca, f.Descripcion, Convert.ToString(f.Pagina, System.Globalization.CultureInfo.InvariantCulture) ?? "", f.SwCode };
        Bornas.Texto = f => f.Marca;
        Bornas.EsPe = f => f.Marca.ToUpperInvariant().StartsWith("PE", StringComparison.Ordinal);

        Elementos.Clave = e => e.Tipo;
        Elementos.Valores = e => new[] { e.Esquema, e.Designacion, e.Tipo, e.PaginasTexto, e.SwCode };
        Elementos.Texto = e => e.Designacion;

        foreach (var lista in new[] { Fuerza, Maniobra })
        {
            lista.Clave = f => f.Color;
            lista.Valores = f => new[] { f.Esquema, f.Numero, f.Color, f.Pagina.ToString(), f.SwCode };
            lista.Texto = f => f.Numero;
            lista.EsPe = f => f.Color.ToUpperInvariant().StartsWith("VE/AM", StringComparison.Ordinal) || f.Numero.ToUpperInvariant().StartsWith("PE", StringComparison.Ordinal);
        }
    }

    public ListaEtiquetas<Cable> Cables(string clase) => clase == "fuerza" ? Fuerza : Maniobra;

    public static string NombreDe(string ruta) => Path.GetFileNameWithoutExtension(ruta);

    public List<string> Esquemas()
    {
        var vistos = new List<string>();
        foreach (var e in Bornas.Filas.Select(f => f.Esquema).Concat(Elementos.Filas.Select(f => f.Esquema))
                                 .Concat(Fuerza.Filas.Select(f => f.Esquema)).Concat(Maniobra.Filas.Select(f => f.Esquema)))
            if (!vistos.Contains(e)) vistos.Add(e);
        return vistos;
    }

    public bool Varios => Esquemas().Count > 1;

    // ---------------------------------------------------------------------
    // Abrir archivos
    // ---------------------------------------------------------------------
    /// <summary>
    /// Antes de leer: separa los formatos desconocidos y los esquemas repetidos. Con <paramref name="anadir"/> = false
    /// empieza de cero.
    /// </summary>
    public List<string> Preparar(IEnumerable<string> rutasEntrada, bool anadir, out List<string> malos, out List<string> repetidos)
    {
        var rutas = rutasEntrada.ToList();
        malos = rutas.Where(r => Lectores.TipoArchivo(r) == null).ToList();
        var malosLocal = malos;
        rutas = rutas.Where(r => !malosLocal.Contains(r)).ToList();
        if (rutas.Count == 0) { repetidos = new List<string>(); return rutas; }
        if (!anadir) Vaciar();
        var ya = Esquemas().ToHashSet();
        repetidos = rutas.Where(r => ya.Contains(NombreDe(r))).ToList();
        var repetidosLocal = repetidos;
        rutas = rutas.Where(r => !repetidosLocal.Contains(r)).ToList();
        if (rutas.Count > 0) RutaActual = rutas[^1];
        return rutas;
    }

    /// <summary>Lee un esquema (PDF, DXF, DWG o imagen). Se puede llamar desde otro hilo.</summary>
    public static ResultadoArchivo Leer(string ruta, Action<string>? progreso = null)
    {
        try
        {
            var paginas = Lectores.LeerPaginas(ruta, progreso);
            progreso?.Invoke("buscando bornas y elementos…");
            var bornas = Extraccion.ExtraerBornas(paginas);
            var elementos = Extraccion.ExtraerElementos(paginas);
            var cables = Extraccion.ExtraerCables(paginas);
            // páginas leídas con OCR que han aportado bornas o elementos (para avisar)
            var usadas = new HashSet<object>();
            foreach (var b in bornas) usadas.Add(b.Pagina);
            foreach (var e in elementos) foreach (var n in e.Paginas) usadas.Add(n);
            var ocr = paginas.Where(p => p.Origen == "ocr" && usadas.Contains(p.Numero)).Select(p => p.Numero).ToList();
            var sinOcr = paginas.Where(p => p.SinOcr).Select(p => p.Numero).ToList();
            var nombre = Extraccion.NumeroEsquema(paginas) ?? NombreDe(ruta);
            return new ResultadoArchivo { Ruta = ruta, Nombre = nombre, Bornas = bornas, Elementos = elementos, Cables = cables, Ocr = ocr, SinOcr = sinOcr };
        }
        catch (ErrorLectura e)
        {
            return new ResultadoArchivo { Ruta = ruta, Nombre = NombreDe(ruta), Error = e.Message };
        }
        catch (Exception e)
        {
            return new ResultadoArchivo { Ruta = ruta, Nombre = NombreDe(ruta), Error = $"No se ha podido leer el archivo:\n{e.Message}" };
        }
    }

    /// <summary>Añade a la sesión lo leído de uno o varios archivos.</summary>
    public ResumenCarga Incorporar(IEnumerable<ResultadoArchivo> resultados)
    {
        var res = new ResumenCarga();
        var nuevasB = new List<Borna>(); var nuevosE = new List<Elemento>(); var nuevosC = new List<Cable>();
        var ya = Esquemas().ToHashSet();
        var lista = resultados.ToList();
        foreach (var r in lista)
        {
            string archivo = Path.GetFileName(r.Ruta);
            if (r.SinOcr.Count > 0)
            {
                string pags = string.Join(", ", r.SinOcr.Take(15)) + (r.SinOcr.Count > 15 ? "…" : "");
                res.AvisosSinOcr.Add($"{archivo}: páginas {pags}");
            }
            if (r.Error != null) { res.Errores.Add($"{archivo}:\n{r.Error}"); continue; }
            if (ya.Contains(r.Nombre)) { res.Errores.Add($"{archivo}:\nel esquema {r.Nombre} ya estaba cargado."); continue; }
            ya.Add(r.Nombre);
            var elementos = Extraccion.SoloPlaca(r.Elementos);
            res.SinNumero += r.Cables.Count(c => c.Numero.Length == 0);
            var cables = r.Cables.Where(c => c.Numero.Length > 0).ToList();    // los hilos sin número no llevan etiqueta
            if (r.Bornas.Count == 0 && elementos.Count == 0 && cables.Count == 0)
            {
                string motivo = r.SinOcr.Count > 0
                    ? "todas las páginas útiles son imagen (escaneadas) y este programa no tiene OCR."
                    : "no he encontrado bornas, elementos ni cables.";
                res.Errores.Add($"{archivo}:\n{motivo}");
                continue;
            }
            foreach (var f in r.Bornas) f.Esquema = r.Nombre;
            foreach (var f in elementos) f.Esquema = r.Nombre;
            foreach (var f in cables) f.Esquema = r.Nombre;
            nuevasB.AddRange(r.Bornas); nuevosE.AddRange(elementos); nuevosC.AddRange(cables);
            res.Resumen.Add($"{r.Nombre}: {r.Bornas.Count} bornas, {elementos.Count} elementos, {cables.Count} cables");
            if (r.Ocr.Count > 0)
            {
                string pags = string.Join(", ", r.Ocr.Take(12)) + (r.Ocr.Count > 12 ? "…" : "");
                res.AvisosOcr.Add($"{r.Nombre}: páginas {pags}");
            }
        }
        res.PaginasSinOcr = lista.Sum(r => r.SinOcr.Count);
        res.HayFilasNuevas = nuevasB.Count > 0 || nuevosE.Count > 0 || nuevosC.Count > 0;
        if (res.HayFilasNuevas)
        {
            CargarBornas(Bornas.Filas.Concat(nuevasB).ToList(), nuevasB);
            CargarElementos(Elementos.Filas.Concat(nuevosE).ToList(), nuevosE);
            CargarCables(nuevosC);
        }
        PonerCopiasPorDefecto();
        if (res.Resumen.Count > 0)
        {
            string nota = res.AvisosSinOcr.Count > 0 ? $"  (sin OCR: no se han leído {res.PaginasSinOcr} páginas sin texto)" : "";
            string aviso = res.SinNumero > 0 ? $"  ({res.SinNumero} hilos sin número no llevan etiqueta)" : "";
            res.Estado = "Listo. " + string.Join(" | ", res.Resumen) + nota + aviso;
        }
        if (res.Errores.Count > 0) res.Estado = "Algún archivo no se ha podido leer.";
        return res;
    }

    private void PonerCopiasPorDefecto()
    {
        var esquemas = Esquemas();
        foreach (var e in esquemas) if (!Copias.ContainsKey(e)) Copias[e] = 1;
        foreach (var k in Copias.Keys.Where(k => !esquemas.Contains(k)).ToList()) Copias.Remove(k);
    }

    /// <summary>Vacía todas las listas.</summary>
    public void Vaciar()
    {
        Bornas.Incluir = new MapaOrdenado<string, bool>(); Bornas.Cargar(new List<Borna>());
        Elementos.Incluir = new MapaOrdenado<string, bool>(); Elementos.Cargar(new List<Elemento>());
        foreach (var l in new[] { Fuerza, Maniobra }) { l.Incluir = new MapaOrdenado<string, bool>(); l.Cargar(new List<Cable>()); }
        Copias.Clear();
    }

    // ---- listados Excel / CSV ----
    /// <summary>Carga un listado ya interpretado. Devuelve el texto de estado, o lanza ErrorLectura si no hay datos.</summary>
    public string IncorporarTabla(string ruta, IReadOnlyList<IReadOnlyList<string>> filas, int? cabecera,
                                  IReadOnlyDictionary<string, int?> columnas, string modo)
    {
        var (bornas, elementos) = Extraccion.FilasDesdeTabla(filas, cabecera, columnas, modo);
        string nombre = NombreDe(ruta), archivo = Path.GetFileName(ruta);
        if (bornas.Count == 0 && elementos.Count == 0)
            throw new ErrorLectura("No he encontrado datos en esas columnas. Revisa la columna elegida.");
        RutaActual = ruta;
        if (modo == "bornas")
        {
            foreach (var f in bornas) f.Esquema = nombre;
            CargarBornas(Bornas.Filas.Concat(bornas).ToList(), bornas);
            PonerCopiasPorDefecto();
            return $"Listo: {bornas.Count} bornas del listado {archivo}.";
        }
        var datos = Extraccion.SoloPlaca(elementos);
        foreach (var f in datos) f.Esquema = nombre;
        CargarElementos(Elementos.Filas.Concat(datos).ToList(), datos);
        PonerCopiasPorDefecto();
        return $"Listo: {datos.Count} elementos del listado {archivo} (sin motores, pilotos ni pulsadores).";
    }

    // ---- SW-CODE (referencia del consumible) ----
    /// <summary>Cada borna nueva toma el SW-CODE que se usó la última vez en su regleta.</summary>
    private void CargarBornas(List<Borna> filas, IEnumerable<Borna> nuevas)
    {
        foreach (var f in nuevas) f.SwCode = Cfg.SwcodeRegletas.GetValueOrDefault(f.Regleta, "");
        Bornas.Cargar(filas);
    }

    private void CargarElementos(List<Elemento> filas, IEnumerable<Elemento> nuevos)
    {
        foreach (var e in nuevos) e.SwCode = Cfg.SwcodeElementos;
        Elementos.Cargar(filas);
    }

    /// <summary>Reparte los cables nuevos en fuerza y maniobra (según su número de hilo) y les pone el SW-CODE recordado.</summary>
    private void CargarCables(List<Cable> nuevos)
    {
        var fuerza = Fuerza.Filas.ToList(); var maniobra = Maniobra.Filas.ToList();
        for (int i = 0; i < nuevos.Count; i++)
        {
            nuevos[i].Orden = i;
            nuevos[i].SwCode = Cfg.SwcodeCables.GetValueOrDefault(nuevos[i].Tipo.ToLowerInvariant(), "");
            (nuevos[i].Tipo.ToLowerInvariant() == "fuerza" ? fuerza : maniobra).Add(nuevos[i]);
        }
        Fuerza.Cargar(fuerza);
        Maniobra.Cargar(maniobra);
    }

    /// <summary>Carga filas ya hechas (con su SW-CODE puesto), sin tocar los códigos. Para el modo demostración.</summary>
    public void CargarFilas(List<Borna> bornas, List<Elemento> elementos)
    {
        RutaActual = "demo";
        CargarBornas(Bornas.Filas.Concat(bornas).ToList(), Array.Empty<Borna>());
        CargarElementos(Elementos.Filas.Concat(elementos).ToList(), Array.Empty<Elemento>());
        PonerCopiasPorDefecto();
    }

    /// <summary>SW-CODE por defecto de los elementos (el último que se usó).</summary>
    public string SwcodeElementos => Cfg.SwcodeElementos;
    public string SwcodeCables(string clase) => Cfg.SwcodeCables.GetValueOrDefault(clase, "");

    public void PonerSwcodeBornas(IEnumerable<Borna> filas, string codigo)
    {
        var lista = filas.ToList();
        ListaEtiquetas<Borna>.PonerSwcode(lista, codigo);
        // Recuerda el SW-CODE de cada regleta (si todas sus bornas tienen el mismo)
        foreach (var regleta in lista.Select(f => f.Regleta).Distinct().ToList())
        {
            var codigos = Bornas.Filas.Where(f => f.Regleta == regleta).Select(f => f.SwCode).Distinct().ToList();
            if (codigos.Count == 1)
            {
                if (codigos[0].Length > 0) Cfg.SwcodeRegletas[regleta] = codigos[0];
                else Cfg.SwcodeRegletas.Remove(regleta);
            }
        }
        Config.Guardar(Cfg);
    }

    public void PonerSwcodeElementos(IEnumerable<Elemento> filas, string codigo)
    {
        ListaEtiquetas<Elemento>.PonerSwcode(filas, codigo);
        // El SW-CODE más usado en los elementos pasa a ser el de por defecto
        var mas = MasUsado(Elementos.Filas.Select(e => e.SwCode));
        if (!string.IsNullOrEmpty(mas)) { Cfg.SwcodeElementos = mas; Config.Guardar(Cfg); }
    }

    public void PonerSwcodeCables(string clase, IEnumerable<Cable> filas, string codigo)
    {
        ListaEtiquetas<Cable>.PonerSwcode(filas, codigo);
        var mas = MasUsado(Cables(clase).Filas.Select(f => f.SwCode));
        if (!string.IsNullOrEmpty(mas)) { Cfg.SwcodeCables[clase] = mas; Config.Guardar(Cfg); }
    }

    /// <summary>El valor más repetido (en un empate, el que apareció antes), como Counter.most_common(1).</summary>
    private static string? MasUsado(IEnumerable<string> valores)
    {
        var cuenta = new MapaOrdenado<string, int>();
        foreach (var v in valores) cuenta[v] = (cuenta.Contiene(v) ? cuenta[v] : 0) + 1;
        string? mejor = null; int n = 0;
        foreach (var (k, c) in cuenta.Pares()) if (c > n) { mejor = k; n = c; }
        return mejor;
    }

    /// <summary>Pasa cables de fuerza a maniobra (o al revés): el reparto automático no es perfecto.</summary>
    public int MoverCables(IEnumerable<Cable> filas, string desde)
    {
        var mover = filas.ToList();
        if (mover.Count == 0) return 0;
        string a = desde == "fuerza" ? "maniobra" : "fuerza";
        var origen = Cables(desde); var destino = Cables(a);
        var ids = mover.ToHashSet();
        var ordenEsq = Esquemas().Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);
        origen.Cargar(origen.Filas.Where(f => !ids.Contains(f)).ToList());
        foreach (var f in mover)
        {
            f.Tipo = a == "fuerza" ? "Fuerza" : "Maniobra";
            f.SwCode = Cfg.SwcodeCables.GetValueOrDefault(a, "");
        }
        var nuevo = destino.Filas.Concat(mover)
            .OrderBy(f => ordenEsq.GetValueOrDefault(f.Esquema, 0)).ThenBy(f => f.Pagina).ThenBy(f => f.Orden).ToList();
        destino.Cargar(nuevo);
        return mover.Count;
    }

    // ---------------------------------------------------------------------
    // Quitar esquemas
    // ---------------------------------------------------------------------
    /// <summary>Quita las bornas, elementos y cables de esos esquemas (y sus casillas de copias).</summary>
    public void Quitar(ISet<string> esquemas)
    {
        // primero se filtran las listas y luego se recargan: el nombre de los filtros de regleta depende de cuántos esquemas quedan
        var b = Bornas.Filas.Where(f => !esquemas.Contains(f.Esquema)).ToList();
        var e = Elementos.Filas.Where(f => !esquemas.Contains(f.Esquema)).ToList();
        var fz = Fuerza.Filas.Where(f => !esquemas.Contains(f.Esquema)).ToList();
        var mn = Maniobra.Filas.Where(f => !esquemas.Contains(f.Esquema)).ToList();
        Bornas.Incluir = new MapaOrdenado<string, bool>(); Bornas.Cargar(b);
        Elementos.Cargar(e); Fuerza.Cargar(fz); Maniobra.Cargar(mn);
        foreach (var x in esquemas) Copias.Remove(x);
    }

    // ---------------------------------------------------------------------
    // Copias y guardado
    // ---------------------------------------------------------------------
    public List<T> ConCopias<T>(IEnumerable<T> filas) where T : class, IFila => Extraccion.Multiplicar(filas, Copias);

    /// <summary>Etiquetas de cable a imprimir: con copias y una para cada extremo del hilo.</summary>
    public List<Cable> EtiquetasCables(string clase) => Extraccion.PorExtremos(ConCopias(Cables(clase).Incluidas()));

    public bool AlgunaCopiaMultiple => Esquemas().Count > 0 && Copias.Values.Any(n => n > 1);

    public int Cuenta(Que que) => que switch
    {
        Que.Bornas => Bornas.Incluidas().Count,
        Que.Elementos => Elementos.Incluidas().Count,
        Que.CablesFuerza => Fuerza.Incluidas().Count,
        Que.CablesManiobra => Maniobra.Incluidas().Count,
        _ => Bornas.Incluidas().Count + Elementos.Incluidas().Count + Fuerza.Incluidas().Count + Maniobra.Incluidas().Count,
    };

    /// <summary>Texto de las etiquetas (una por línea), con copias y extremos.</summary>
    public List<string> Textos(Que que)
    {
        var bornas = ConCopias(Bornas.Incluidas()).Select(f => f.Marca).ToList();
        var elementos = ConCopias(Elementos.Incluidas()).Select(e => e.Designacion).ToList();
        var fuerza = EtiquetasCables("fuerza").Select(c => c.Numero).ToList();
        var maniobra = EtiquetasCables("maniobra").Select(c => c.Numero).ToList();
        return que switch
        {
            Que.Bornas => bornas,
            Que.Elementos => elementos,
            Que.CablesFuerza => fuerza,
            Que.CablesManiobra => maniobra,
            _ => bornas.Concat(elementos).Concat(fuerza).Concat(maniobra).ToList(),
        };
    }

    public static string NombreQue(Que que) => que switch
    {
        Que.Bornas => "bornas",
        Que.Elementos => "elementos",
        Que.CablesFuerza => "cables fuerza",
        Que.CablesManiobra => "cables maniobra",
        _ => "todo",
    };

    /// <summary>Nombre de archivo propuesto: «esquema_que.xlsx» o «N_esquemas_que.xlsx».</summary>
    public string NombreExcelPropuesto(Que que)
    {
        var esquemas = Esquemas();
        string baseNombre = esquemas.Count == 1 ? esquemas[0] : $"{esquemas.Count}_esquemas";
        return $"{baseNombre}_{NombreQue(que).Replace(' ', '_')}.xlsx";
    }

    /// <summary>Guarda lo de la pestaña indicada: solo bornas, solo elementos, solo cables o todo.</summary>
    public void GuardarExcel(Que que, string ruta)
    {
        // cada esquema repetido tantas veces como diga su casilla «Copias», seguido
        switch (que)
        {
            case Que.Bornas: ExcelEtiquetas.GuardarBornas(ConCopias(Bornas.Incluidas()), ruta); break;
            case Que.Elementos: ExcelEtiquetas.GuardarElementos(ConCopias(Elementos.Incluidas()), ruta); break;
            case Que.CablesFuerza: ExcelEtiquetas.GuardarCables(EtiquetasCables("fuerza"), ruta, "fuerza"); break;
            case Que.CablesManiobra: ExcelEtiquetas.GuardarCables(EtiquetasCables("maniobra"), ruta, "maniobra"); break;
            default:
                ExcelEtiquetas.GuardarTodo(ConCopias(Bornas.Incluidas()), ConCopias(Elementos.Incluidas()), ruta,
                                           EtiquetasCables("fuerza"), EtiquetasCables("maniobra"));
                break;
        }
    }

    /// <summary>Línea de resumen: «N bornas · N elementos · …» (con copias y extremos si hay más de una copia).</summary>
    public string ResumenEtiquetas()
    {
        if (Esquemas().Count == 0) return "";
        int nb = Bornas.Incluidas().Count, ne = Elementos.Incluidas().Count, nf = Fuerza.Incluidas().Count, nm = Maniobra.Incluidas().Count;
        string texto = $"{nb} bornas · {ne} elementos · {nf} cables fuerza · {nm} cables maniobra";
        if (AlgunaCopiaMultiple)
        {
            int nb2 = ConCopias(Bornas.Incluidas()).Count, ne2 = ConCopias(Elementos.Incluidas()).Count;
            int nf2 = EtiquetasCables("fuerza").Count, nm2 = EtiquetasCables("maniobra").Count;
            texto += $"  →  con copias y extremos: {nb2} + {ne2} + {nf2} + {nm2}";
        }
        return texto;
    }

    /// <summary>Texto de la cabecera: el esquema, o la lista de esquemas.</summary>
    public string Cabecera()
    {
        var esquemas = Esquemas();
        if (esquemas.Count == 0) return "Abre uno o varios esquemas, o arrástralos encima del programa.";
        if (esquemas.Count == 1) return esquemas[0];
        string texto = $"{esquemas.Count} esquemas: " + string.Join(", ", esquemas);
        return texto.Length > 110 ? texto[..107] + "…" : texto;
    }
}
