# Generador de etiquetas .NET

**Lee el esquema eléctrico de un cuadro y genera las etiquetas para marcar bornas, aparatos y cables, listas para importar en GeniusPro (Cembre).**

![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![Avalonia 11](https://img.shields.io/badge/UI-Avalonia%2011-8B44AC)

## El problema

Al montar un cuadro eléctrico hay que etiquetar cada borna de las regletas, cada aparato (contactores, magnetotérmicos, relés…) y cada extremo de cada cable. Esa información ya está en el esquema, pero sacarla a mano y pasarla a la impresora de etiquetas es un trabajo lento y fácil de equivocar.

Este programa lo hace solo: le das el esquema y devuelve un Excel con todas las etiquetas, en el formato que importa GeniusPro, el software de etiquetado de Cembre.

## Qué hace

1. **Lee el esquema** en cualquiera de estos formatos:
   - PDF del programa de esquemas (el más fiable)
   - AutoCAD DXF
   - AutoCAD DWG (necesita [ODA File Converter](https://www.opendesign.com/guestfiles/oda_file_converter), gratuito)
   - Listados Excel o CSV exportados de EPLAN, SEE, AutoCAD Electrical…
2. **Interpreta el contenido** y extrae tres tipos de etiquetas:
   - **Bornas**: la marca de cada borna de cada regleta.
   - **Elementos**: las designaciones de los aparatos montados en la placa (Q100, KM100, K20…). Los de la puerta o los de fuera del armario no llevan etiqueta.
   - **Cables**: cada hilo con su número y su color. Con el dibujo vectorial del PDF sigue la línea del hilo hasta sus dos extremos (una borna, una regleta, una referencia a otra hoja) y genera una etiqueta por cada extremo.
3. **Genera el Excel** con una hoja lista para importar en GeniusPro, otra con el detalle (regleta, posición, página del esquema…) y un resumen. Cada bloque (esquema y copia) lleva un fondo alterno para ver de un vistazo dónde empieza y dónde acaba.

La ventana permite cargar varios esquemas a la vez, indicar cuántas **copias** de etiquetas quieres de cada uno, poner el **SW-CODE**, mover cables entre las listas de fuerza y maniobra, y copiar o guardar el resultado.

## Cómo está hecho

La lógica está separada de la interfaz, y cada capa tiene una sola responsabilidad:

```
src/
├─ Etiquetador.Core   Lógica, sin dependencias de interfaz
│  ├─ Lectura/        Un lector por formato (PDF, DXF/DWG, tablas) detrás de LectorArchivos
│  ├─ Extraccion/     Interpretación del esquema: bornas, elementos, cables, copias
│  ├─ Excel/          Lector y escritor de .xlsx propio, y el formato de GeniusPro
│  ├─ Aplicacion/     Sesión de trabajo: listas, copias, SW-CODE
│  └─ Modelos/        Datos puros (Borna, Elemento, Cable, Pagina…)
├─ Etiquetador.Cli   Línea de comandos
└─ Etiquetador.App   Ventana Avalonia 11, hecha con componentes pequeños
```

El flujo es `LectorArchivos` → páginas (texto y dibujo) → `Extractor*` → `Sesion` → `ExcelEtiquetas` → `.xlsx`.

- `Core` nunca depende de `App`: toda la lógica nueva va a `Core`.
- Los lectores de DXF y de tablas Excel/CSV están escritos desde cero, sin librerías externas. El PDF se lee con [PdfPig](https://github.com/UglyToad/PdfPig).
- Este proyecto es un port a .NET 8 de la primera versión, escrita en Python. Sobre los casos de prueba, el JSON y el Excel generados son idénticos a los de aquella versión.

Más detalle en [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md) y [`docs/NOTAS-PORT.md`](docs/NOTAS-PORT.md).

## Usarlo

### Con doble clic (Windows)
Ejecuta `construir.bat`: compila el programa en `publish\EtiquetadorCuadros.exe` (funciona sin tener .NET instalado) y crea un acceso directo en el escritorio. Para compilarlo hace falta el SDK de .NET 8.

### Desde el código
```bash
dotnet restore
dotnet run --project src/Etiquetador.App                 # ventana
dotnet run --project src/Etiquetador.App -- --demo       # con datos de ejemplo
dotnet run --project src/Etiquetador.App -- --prueba     # autotest
```

### Línea de comandos
```bash
dotnet run --project src/Etiquetador.Cli -- --json ESQUEMA SALIDA.json   # bornas, elementos y cables en JSON
dotnet run --project src/Etiquetador.Cli -- --xlsx ESQUEMA SALIDA.xlsx   # el Excel «todo»
dotnet run --project src/Etiquetador.Cli -- --prueba ESQUEMA             # informe de lo que ha leído
```

La configuración se guarda en `%APPDATA%\EtiquetadorCuadros\config.json` y los errores inesperados, en `error.log` de esa misma carpeta.

## Limitaciones

- **PDF escaneados e imágenes:** el reconocimiento de texto (OCR) de la versión Python no está incluido en esta versión; se avisa con un mensaje claro en vez de leerlos.
- **DXF binario:** no se admite; hay que guardarlo como DXF de texto (ASCII).
- **DWG:** depende de ODA File Converter instalado.
- **Datos de prueba:** el software de esquemas de origen solo está disponible para clientes, por eso el repositorio no incluye esquemas de ejemplo (`-- --demo` carga datos de muestra).
