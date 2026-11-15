using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Etiquetador.Core;

// Línea de comandos del etiquetador (sin ventana). Sirve para comprobar la lectura y comparar con la versión Python.
//   EtiquetadorCli --prueba ARCHIVO            escribe ARCHIVO.prueba.txt (mismo formato que «EtiquetadorCuadros --prueba»)
//   EtiquetadorCli --json ARCHIVO SALIDA.json  vuelca bornas, elementos y cables a JSON
//   EtiquetadorCli --xlsx ARCHIVO SALIDA.xlsx  guarda el Excel «todo» (bornas + elementos + cables)

static JsonNode? Nodo(object? v) => v switch
{
    null => null,
    int i => JsonValue.Create(i),
    double d => JsonValue.Create(d),
    bool b => JsonValue.Create(b),
    _ => JsonValue.Create(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)),
};

static (List<Pagina> Paginas, List<Borna> Bornas, List<Elemento> Elementos, List<Cable> Cables) Leer(string ruta)
{
    var paginas = LectorArchivos.LeerPaginas(ruta, _ => { });
    var bornas = ExtractorBornas.Extraer(paginas);
    var elementos = ExtractorElementos.SoloPlaca(ExtractorElementos.Extraer(paginas));
    var cables = ExtractorCables.Extraer(paginas);
    return (paginas, bornas, elementos, cables);
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: EtiquetadorCli --prueba|--json|--xlsx ARCHIVO [SALIDA]");
    return 2;
}

string modo = args[0], archivo = args[1];
try
{
    switch (modo)
    {
        case "--prueba":
        {
            var lineas = new List<string>();
            try
            {
                var (paginas, bornas, elementos, cablesTodos) = Leer(archivo);
                var cables = cablesTodos.Where(c => c.Numero.Length > 0).ToList();
                lineas.Add($"cables_fuerza={cables.Count(c => c.Tipo == "Fuerza")}");
                lineas.Add($"cables_maniobra={cables.Count(c => c.Tipo == "Maniobra")}");
                lineas.Add($"paginas={paginas.Count}");
                lineas.Add("ocr=[" + string.Join(", ", paginas.Where(p => p.Origen == "ocr").Select(p => p.Numero)) + "]");
                lineas.Add($"bornas={bornas.Count}");
                lineas.Add($"elementos_placa={elementos.Count}");
                lineas.Add("primeras_bornas=" + string.Join(",", bornas.Take(10).Select(b => b.Marca)));
                lineas.Add("primeros_elementos=" + string.Join(",", elementos.Take(10).Select(e => e.Designacion)));
            }
            catch (Exception e) { lineas.Add($"ERROR: {e.Message}"); }
            File.WriteAllText(archivo + ".prueba.txt", string.Join("\n", lineas) + "\n");
            return 0;
        }
        case "--json" when args.Length >= 3:
        {
            var (paginas, bornas, elementos, cables) = Leer(archivo);
            var raiz = new JsonObject
            {
                ["paginas"] = paginas.Count,
                ["numero_esquema"] = Nodo(IdentificadorEsquema.Buscar(paginas)),
                ["bornas"] = new JsonArray(bornas.Select(b => (JsonNode?)new JsonObject
                {
                    ["regleta"] = b.Regleta, ["pos"] = b.Pos, ["marca"] = b.Marca, ["descripcion"] = b.Descripcion, ["pagina"] = Nodo(b.Pagina),
                }).ToArray()),
                ["elementos"] = new JsonArray(elementos.Select(e => (JsonNode?)new JsonObject
                {
                    ["designacion"] = e.Designacion, ["tipo"] = e.Tipo, ["ubicacion"] = e.Ubicacion,
                    ["paginas"] = new JsonArray(e.Paginas.Select(Nodo).ToArray()),
                }).ToArray()),
                ["cables"] = new JsonArray(cables.Select(c => (JsonNode?)new JsonObject
                {
                    ["numero"] = c.Numero, ["color"] = c.Color, ["tipo"] = c.Tipo, ["origen"] = c.Origen, ["destino"] = c.Destino,
                    ["dudoso"] = c.Dudoso, ["pagina"] = c.Pagina,
                }).ToArray()),
            };
            File.WriteAllText(args[2], raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return 0;
        }
        case "--tabla" when args.Length >= 3:
        {
            var filas = LectorTabla.Leer(archivo);
            var an = ImportadorTabla.Analizar(filas.Select(f => (IReadOnlyList<string>)f).ToList());
            var (bs, es) = ImportadorTabla.FilasDesde(filas.Select(f => (IReadOnlyList<string>)f).ToList(), an.Cabecera, an.Columnas, an.Modo);
            var raiz = new JsonObject
            {
                ["filas"] = new JsonArray(filas.Select(f => (JsonNode?)new JsonArray(f.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())).ToArray()),
                ["cabecera"] = Nodo(an.Cabecera),
                ["columnas"] = new JsonObject(an.Columnas.Select(kv => KeyValuePair.Create(kv.Key, Nodo(kv.Value)))),
                ["modo"] = an.Modo,
                ["bornas"] = new JsonArray(bs.Select(b => (JsonNode?)new JsonObject
                {
                    ["regleta"] = b.Regleta, ["pos"] = b.Pos, ["marca"] = b.Marca, ["descripcion"] = b.Descripcion, ["pagina"] = Nodo(b.Pagina),
                }).ToArray()),
                ["elementos"] = new JsonArray(es.Select(e => (JsonNode?)new JsonObject
                {
                    ["designacion"] = e.Designacion, ["tipo"] = e.Tipo, ["ubicacion"] = e.Ubicacion,
                    ["paginas"] = new JsonArray(e.Paginas.Select(Nodo).ToArray()),
                }).ToArray()),
            };
            File.WriteAllText(args[2], raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return 0;
        }
        case "--xlsx" when args.Length >= 3:
        {
            var (paginas, bornas, elementos, cablesTodos) = Leer(archivo);
            var cables = cablesTodos.Where(c => c.Numero.Length > 0).ToList();
            string esq = IdentificadorEsquema.Buscar(paginas) ?? Path.GetFileNameWithoutExtension(archivo);
            foreach (var f in bornas) f.Esquema = esq;
            foreach (var f in elementos) { f.Esquema = esq; f.SwCode = Config.SwcodeElementosDefecto; }
            foreach (var f in cables) f.Esquema = esq;
            var fuerza = Multiplicador.PorExtremos(cables.Where(c => c.Tipo == "Fuerza"));
            var maniobra = Multiplicador.PorExtremos(cables.Where(c => c.Tipo == "Maniobra"));
            ExcelEtiquetas.GuardarTodo(bornas, elementos, args[2], fuerza, maniobra);
            return 0;
        }
        default:
            Console.Error.WriteLine("Opción no reconocida.");
            return 2;
    }
}
catch (ErrorLectura e)
{
    Console.Error.WriteLine("ERROR: " + e.Message);
    return 1;
}
