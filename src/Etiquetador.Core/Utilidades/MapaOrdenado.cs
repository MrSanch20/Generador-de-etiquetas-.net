namespace Etiquetador.Core.Utilidades;

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
