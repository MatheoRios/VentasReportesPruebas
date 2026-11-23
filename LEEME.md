# VentasReportesPruebas

Proyecto independiente (`c:\app server\project C#\VentasReportesPruebas`) para ejecutar pruebas parametrizadas de todos los reportes de `VentasReportesApi` contra una tienda, varias tiendas, todas las tiendas o una instancia SQL directa.

## Uso rápido

Desde la carpeta `VentasReportesPruebas`:

```powershell
# Ver ayuda completa
dotnet run -- --ayuda

# Abrir menú interactivo (sin argumentos)
dotnet run

# Cruce automático Resumen vs Diario vs Libro IVA
dotnet run -- -r cruce -t TURMERO --desde ayer

# Auditoría completa de las 25 tiendas (conexión, proc POS, reloj, seriales y NoFiscal)
dotnet run -- -r auditoria --todas --desde ayer

# Ver tiendas configuradas en VentasReportesApi (seguridad.db)
dotnet run -- --listar-tiendas

# Libro de IVA de ayer para TURMERO
dotnet run -- -r libroiva -t TURMERO --desde ayer

# Libro de IVA contra una instancia directa (p. ej. Maracay)
$env:PRUEBA_CLAVE = "TU_CLAVE"
dotnet run -- -r libroiva --servidor 172.25.20.10 --puerto 59241 --usuario reportsRetail --desde 2026-11-19

# Smoke test de todos los reportes principales en una tienda
dotnet run -- -r todo -t TURMERO --desde ayer

# Resumen de todas las tiendas exportado a CSV para Excel
dotnet run -- -r resumen --todas --desde ayer --csv resumen-ayer.csv

# Ventas por categoría con filtros
dotnet run -- -r categorias -t TURMERO --desde 2026-11-19 --caja 000001 --no-fiscal 0
```

## Reportes soportados (`--reporte` / `-r`)
`libroiva`, `ventas`, `resumen`, `diario`, `categorias`, `cierres`, `listado`, `entradas`, `productos`, `tasa`, `compras`, `ultimoscostos`, `todo`.
