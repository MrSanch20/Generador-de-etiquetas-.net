# Arquitectura

Tres proyectos. La lógica vive en `Core` (sin dependencias de interfaz); `Cli` y `App` solo la usan.

```
src/
├─ Etiquetador.Core        Lógica (namespace Etiquetador.Core.*)
│  ├─ Modelos/             Datos puros: Borna, Elemento, Cable, Pagina, TextoPos, Geometria (Trazo, Flecha…), IFila, ErrorLectura
│  ├─ Lectura/             Entrada de archivos: LectorArchivos (reparte por extensión), LectorPdf, LectorDxf (+DWG, repartido en Lectura/Dxf: analizador, extractor de textos, separador de hojas, conversor DWG), LectorTabla (Excel/CSV), LectorOcr
│  ├─ Extraccion/          Interpretación del esquema
│  │    ExtractorBornas · ExtractorElementos · ExtractorCables (+ SeguimientoDeHilos)
│  │    ImportadorTabla · IdentificadorEsquema · Multiplicador
│  │    Designaciones (tipos y orden de KM100, Q20…) · TitulosPagina · Medidas
│  ├─ Excel/               LibroXlsx (lector/escritor xlsx propio) y ExcelEtiquetas (formato GeniusPro)
│  ├─ Configuracion/       Config (valores por defecto)
│  ├─ Aplicacion/          Sesion (estado), CargadorEsquemas (leer un esquema entero), ListaEtiquetas, ResultadoArchivo, ResumenCarga, Que, Demo
│  └─ Utilidades/          MapaOrdenado, Rx (regex), Redondeo (el .5 al par, como Python)
├─ Etiquetador.Cli         Línea de comandos / pruebas
└─ Etiquetador.App         Ventana Avalonia: MainWindow (monta y conecta) + componentes PanelCopias, VistaTodo, ListaView, TablaView,
                           DialogoTabla, Dialogos y servicios ExportadorEtiquetas (guardar/copiar) y SelectorArchivos
```

Flujo: `LectorArchivos` → páginas (texto + dibujo) → `Extractor*` → `Sesion` (listas) → `ExcelEtiquetas` → .xlsx.

Regla: `Core` nunca depende de `App`. Toda la lógica nueva va a `Core`.

Comprobación del refactor: el resultado (JSON y Excel) sobre los PDF/DXF de prueba es idéntico byte a byte al de antes de reestructurar.
