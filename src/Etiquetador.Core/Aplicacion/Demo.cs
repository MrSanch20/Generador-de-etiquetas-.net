namespace Etiquetador.Core.Aplicacion;

/// <summary>
/// Datos de ejemplo INVENTADOS, para enseñar el programa sin usar esquemas reales:
///   EtiquetadorCuadros --demo
/// </summary>
public static class Demo
{
    private static List<(string, string)> Fuerza(int n0, string[] motores)
    {
        var res = new List<(string, string)>();
        for (int k = 0; k < motores.Length; k++)
        {
            int n = n0 + k;
            res.Add(($"U{n}", motores[k])); res.Add(($"V{n}", motores[k]));
            res.Add(($"W{n}", motores[k])); res.Add(($"PE{n}", motores[k]));
        }
        return res;
    }

    private static List<Borna> Bornas(string esquema, (string Regleta, List<(string, string)> Marcas)[] regletas)
    {
        var filas = new List<Borna>();
        string[] senal = { "XS", "XE", "X24" };
        for (int pag = 0; pag < regletas.Length; pag++)
        {
            var (reg, marcas) = regletas[pag];
            for (int i = 0; i < marcas.Count; i++)
                filas.Add(new Borna
                {
                    Esquema = esquema, Regleta = reg, Pos = i + 1, Marca = marcas[i].Item1, Descripcion = marcas[i].Item2,
                    Pagina = pag + 2, SwCode = senal.Contains(reg) ? "212" : "205",
                });
        }
        return filas;
    }

    private static List<Elemento> Elementos(string esquema, IEnumerable<(string, int)> lista) =>
        lista.Select(x => new Elemento
        {
            Esquema = esquema, Designacion = x.Item1, Tipo = Designaciones.TipoEquipo(x.Item1), Ubicacion = "Placa",
            Paginas = new List<object> { x.Item2 }, SwCode = "181",
        }).ToList();

    public static (List<Borna> Bornas, List<Elemento> Elementos) Datos()
    {
        var acometida = new List<(string, string)> { ("PE", ""), ("N", "ACOMETIDA"), ("L1", "ACOMETIDA"), ("L2", "ACOMETIDA"), ("L3", "ACOMETIDA") };
        var sensores = Enumerable.Range(1, 4).SelectMany(i => new[] { "1", "2" }.Select(a => ($"{a}S{i}", $"SENSOR {i}"))).ToList();
        sensores.Add(("PANT", "PANTALLA"));
        var bornas = Bornas("CECE00000000001", new[]
        {
            ("XACO", acometida),
            ("XM", Fuerza(100, new[] { "BOMBA 1", "BOMBA 2", "VENTILADOR 1", "VENTILADOR 2", "CINTA 1", "CINTA 2" })),
            ("XS", sensores),
            ("X24", new List<(string, string)> { ("24V", "MANDO"), ("0V", "MANDO") }),
        }).Concat(Bornas("CECE00000000002", new[]
        {
            ("XACO", acometida),
            ("XM", Fuerza(200, new[] { "COMPRESOR", "EXTRACTOR 1", "EXTRACTOR 2" })),
            ("XE", Enumerable.Range(1, 8).Select(i => (i.ToString(), "ENTRADA DIGITAL")).ToList()),
        })).ToList();

        var e1 = new List<(string, int)> { ("Q0", 3), ("F1", 3), ("F2", 3), ("U1", 4) };
        e1.AddRange(Enumerable.Range(0, 6).Select(i => ($"Q{100 + i}", 5)));
        e1.AddRange(Enumerable.Range(0, 6).Select(i => ($"KM{100 + i}", 5)));
        e1.AddRange(new[] { ("K1", 6), ("K2", 6), ("K3", 6), ("KA1", 6) });
        var e2 = new List<(string, int)> { ("Q0", 3), ("F1", 3), ("U1", 4) };
        e2.AddRange(Enumerable.Range(0, 3).Select(i => ($"Q{200 + i}", 5)));
        e2.AddRange(Enumerable.Range(0, 3).Select(i => ($"KM{200 + i}", 5)));
        e2.AddRange(new[] { ("K10", 6), ("K11", 6) });
        var elementos = Elementos("CECE00000000001", e1).Concat(Elementos("CECE00000000002", e2)).ToList();
        return (bornas, elementos);
    }

    /// <summary>Carga los datos de ejemplo en la sesión.</summary>
    public static void Cargar(Sesion s)
    {
        var (b, e) = Datos();
        s.CargarFilas(b, e);
    }
}
