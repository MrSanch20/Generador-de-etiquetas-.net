using System.Text.RegularExpressions;

namespace Etiquetador.Core;

/// <summary>Error con un mensaje pensado para el usuario.</summary>
public class ErrorLectura : Exception
{
    public ErrorLectura(string mensaje) : base(mensaje) { }
}

/// <summary>Dirección de un texto (con la Y hacia abajo, como en pantalla), redondeada a 1 decimal.</summary>
public readonly record struct Direccion(double X, double Y)
{
    public static readonly Direccion Horizontal = new(1.0, 0.0);
    /// <summary>Girado 90°, se lee de abajo arriba.</summary>
    public static readonly Direccion Vertical = new(0.0, -1.0);

    public static Direccion Redondeada(double x, double y) =>
        new(Math.Round(x, 1, MidpointRounding.ToEven) + 0.0, Math.Round(y, 1, MidpointRounding.ToEven) + 0.0);
}

/// <summary>Color (r, g, b) de 0 a 1.</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public Rgb Redondeado() => new(Math.Round(R, 2), Math.Round(G, 2), Math.Round(B, 2));
}

/// <summary>Línea recta del dibujo vectorial de una página.</summary>
public readonly record struct Trazo(double X0, double Y0, double X1, double Y1, Rgb Color);

/// <summary>Flecha de referencia a otra hoja (triángulo relleno).</summary>
public readonly record struct Flecha(double X, double Y, Rgb Color);

/// <summary>Borna dibujada como circulito.</summary>
public readonly record struct Circulo(double X0, double Y0, double X1, double Y1);

/// <summary>Un texto con su posición. Coordenadas con el origen arriba a la izquierda.</summary>
public sealed class TextoPos
{
    public string Texto { get; init; } = "";
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public Direccion Dir { get; init; } = Direccion.Horizontal;
    /// <summary>Tamaño de letra.</summary>
    public double Tam { get; init; }
    /// <summary>Lo que distingue el estilo del texto: color (int 0xRRGGBB) en PDF; (alto, capa, color) en DXF; "ocr" en OCR.</summary>
    public object? Estilo { get; init; }

    public TextoPos() { }

    public TextoPos(string texto, double x0, double y0, double x1, double y1, Direccion dir, double tam, object? estilo)
    {
        Texto = texto; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Dir = dir; Tam = tam; Estilo = estilo;
    }

    /// <summary>¿El estilo es el color entero indicado? (solo PDF)</summary>
    public bool EsColor(int color) => Estilo is int c && c == color;
}

public sealed class Pagina
{
    public int Numero { get; }
    public double Ancho { get; }
    public double Alto { get; }
    /// <summary>Textos agrupados por línea.</summary>
    public List<TextoPos> Lineas { get; } = new();
    /// <summary>Textos sueltos (spans).</summary>
    public List<TextoPos> Trozos { get; } = new();
    /// <summary>pdf | dxf | ocr</summary>
    public string Origen { get; set; } = "pdf";
    /// <summary>Página escaneada que no se ha podido leer (falta el OCR).</summary>
    public bool SinOcr { get; set; }
    // Dibujo vectorial (solo PDF con texto; sirve para seguir los cables hasta sus extremos).
    public List<Trazo> Trazos { get; } = new();
    public List<Flecha> Flechas { get; } = new();
    public List<Circulo> Circulos { get; } = new();

    public Pagina(int numero, double ancho, double alto, string origen = "pdf")
    {
        Numero = numero; Ancho = ancho; Alto = alto; Origen = origen;
    }

    /// <summary>Todo el texto cuyo centro está entre dos alturas, unido con espacios.</summary>
    public string TextoZona(double yDesde = 0.0, double? yHasta = null)
    {
        double hasta = yHasta ?? Alto;
        return string.Join(" ", Trozos.Where(t => yDesde <= (t.Y0 + t.Y1) / 2 && (t.Y0 + t.Y1) / 2 <= hasta).Select(t => t.Texto));
    }
}

// ---------------------------------------------------------------------------
// Etiquetas
// ---------------------------------------------------------------------------

/// <summary>Una borna de una regleta.</summary>
public sealed class Borna : IFila
{
    public string Regleta { get; set; } = "";
    public int Pos { get; set; }
    public string Marca { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>Número de página (int) o "fila N" (string) si viene de un listado.</summary>
    public object Pagina { get; set; } = 0;
    public string Esquema { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public Borna Clonar() => (Borna)MemberwiseClone();
    public IFila Duplicar() => Clonar();
}

/// <summary>Un elemento del esquema (KM100, Q100…).</summary>
public sealed class Elemento : IFila
{
    public string Designacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    /// <summary>Páginas (int) o filas ("fila N") donde aparece.</summary>
    public List<object> Paginas { get; set; } = new();
    public string Esquema { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public string PaginasTexto => string.Join(", ", Paginas.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));

    public Elemento Clonar()
    {
        var e = (Elemento)MemberwiseClone();
        e.Paginas = new List<object>(Paginas);
        return e;
    }
    public IFila Duplicar() => Clonar();
}

/// <summary>Un hilo del esquema (cable) con su número y color.</summary>
public sealed class Cable : IFila
{
    public string Numero { get; set; } = "";
    public string Color { get; set; } = "";
    /// <summary>"Fuerza" o "Maniobra" ("" si no tiene número).</summary>
    public string Tipo { get; set; } = "";
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public bool Dudoso { get; set; }
    public int Pagina { get; set; }
    public string Esquema { get; set; } = "";
    public int Orden { get; set; }
    /// <summary>"1/2" o "2/2" (una etiqueta para cada punta); "" si aún no se ha separado por extremos.</summary>
    public string Extremo { get; set; } = "";
    public string Copia { get; set; } = "1/1";
    public string SwCode { get; set; } = "";

    public Cable Clonar() => (Cable)MemberwiseClone();
    public IFila Duplicar() => Clonar();
}

// ---------------------------------------------------------------------------
// Utilidades
// ---------------------------------------------------------------------------

/// <summary>Diccionario que conserva el orden de inserción (como el dict de Python).</summary>
public sealed class MapaOrdenado<TK, TV> where TK : notnull
{
    private readonly List<TK> _claves = new();
    private readonly Dictionary<TK, TV> _datos = new();

    public int Count => _claves.Count;
    public IReadOnlyList<TK> Claves => _claves;
    public bool Contiene(TK k) => _datos.ContainsKey(k);
    public bool TryGetValue(TK k, out TV v) => _datos.TryGetValue(k, out v!);

    public TV this[TK k]
    {
        get => _datos[k];
        set
        {
            if (!_datos.ContainsKey(k)) _claves.Add(k);
            _datos[k] = value;
        }
    }

    public bool Quitar(TK k)
    {
        if (!_datos.Remove(k)) return false;
        _claves.Remove(k);
        return true;
    }

    public IEnumerable<KeyValuePair<TK, TV>> Pares() => _claves.Select(k => new KeyValuePair<TK, TV>(k, _datos[k]));
}

/// <summary>Atajos de expresiones regulares con la semántica de re.fullmatch / re.match / re.search de Python.</summary>
public static class Rx
{
    public static Regex Crear(string patron, RegexOptions opciones = RegexOptions.None) =>
        new(patron, opciones | RegexOptions.CultureInvariant);

    /// <summary>re.fullmatch(patrón, texto): la coincidencia ocupa todo el texto.</summary>
    public static bool Completa(Regex re, string texto)
    {
        // Se vuelve a evaluar con anclas: new Regex(@"\A(?:patrón)\z")
        return Anclada(re).IsMatch(texto);
    }

    private static readonly Dictionary<Regex, Regex> _cacheAnclada = new();

    private static Regex Anclada(Regex re)
    {
        lock (_cacheAnclada)
        {
            if (!_cacheAnclada.TryGetValue(re, out var a))
            {
                a = new Regex(@"\A(?:" + re.ToString() + @")\z", re.Options);
                _cacheAnclada[re] = a;
            }
            return a;
        }
    }

    /// <summary>Resultado de re.fullmatch, con sus grupos (o null).</summary>
    public static Match? CompletaMatch(Regex re, string texto)
    {
        var m = Anclada(re).Match(texto);
        return m.Success ? m : null;
    }

    /// <summary>re.match(patrón, texto): coincide al principio del texto.</summary>
    public static Match? Principio(Regex re, string texto)
    {
        var m = new Regex(@"\A(?:" + re.ToString() + ")", re.Options).Match(texto);
        return m.Success ? m : null;
    }
}
