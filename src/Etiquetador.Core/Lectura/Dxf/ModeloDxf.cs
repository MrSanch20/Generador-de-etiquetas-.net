using System.Globalization;

namespace Etiquetador.Core.Lectura.Dxf;

// Estructura en memoria de un DXF: entidades, bloques y documento.
internal sealed class EntidadDxf
{
    public string Tipo = "";
    public List<(int Cod, string Val)> Datos = new();
    public List<EntidadDxf> Hijos = new();      // ATTRIB de un INSERT, VERTEX de una POLYLINE

    public string? Str(int cod) { foreach (var (c, v) in Datos) if (c == cod) return v; return null; }
    public double Num(int cod, double defecto = 0)
    {
        foreach (var (c, v) in Datos)
            if (c == cod && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return defecto;
    }
    public bool Tiene(int cod) { foreach (var (c, _) in Datos) if (c == cod) return true; return false; }
    public int Ent32(int cod, int defecto = 0) => (int)Math.Round(Num(cod, defecto));
    public IEnumerable<string> Todos(int cod) => Datos.Where(d => d.Cod == cod).Select(d => d.Val);
}

internal sealed class BloqueDxf
{
    public string Nombre = "";
    public double BaseX, BaseY;
    public List<EntidadDxf> Entidades = new();
}

internal sealed class DocumentoDxf
{
    public List<EntidadDxf> Entidades = new();
    public Dictionary<string, BloqueDxf> Bloques = new(StringComparer.OrdinalIgnoreCase);
    public List<BloqueDxf> OrdenBloques = new();
}
