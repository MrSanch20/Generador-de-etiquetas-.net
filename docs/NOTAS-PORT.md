# Notas del port

## Verificado (en Linux, contra la versión Python)
- Lectura de PDF, DXF y tablas: JSON idéntico al de Python en los casos de prueba.
- Excel generado: idéntico celda a celda (solo cambia la dimensión de una columna SW-CODE vacía).
- Capa `Sesion` probada con un smoke test (copias, SW-CODE, mover cables, guardar Excel).

## Pendiente / limitaciones
- La ventana Avalonia se ha probado a mano en Windows con PDF reales (cargar, añadir, quitar esquemas, pestañas, copias). Compila y funciona.
- Se fuerza el dibujado por software (`Win32RenderingMode.Software`) y las tablas se rehacen en cada repintado: con el dibujado por GPU y cambiando `ItemsSource` en una lista ya dibujada el proceso se cerraba sin mensaje (Avalonia 11.2.3).
- Los errores no controlados se apuntan en `%APPDATA%\EtiquetadorCuadros\error.log`.
- NuGet (PdfPig, Avalonia) no se pudo restaurar aquí: PdfPig está fijado a `0.1.17-alpha-202611100027-21de1` (la nightly con la que se verificó el Core).
- OCR / PDF escaneados / imágenes: no portado (`IMotorOcr` es un stub que da un mensaje claro).
- DXF: parser propio, probado solo con DXF ASCII generados con ezdxf. DXF binario no soportado. DWG requiere ODA File Converter (sin probar).
- Detección de separador CSV distinta de `csv.Sniffer` de Python.
- No probado en Windows.
