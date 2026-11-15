namespace Etiquetador.Core.Aplicacion;

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
