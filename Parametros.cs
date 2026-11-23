using System.Globalization;

namespace VentasReportesPruebas;

internal sealed class Parametros
{
    public string Reporte { get; set; } = "libroiva";
    public List<string> Tiendas { get; } = new();
    public bool Todas { get; set; }
    public DateTime Desde { get; set; } = DateTime.Today.AddDays(-1);
    public DateTime Hasta { get; set; } = DateTime.Today.AddDays(-1);

    // Conexión directa (opcional, para probar una instancia que aún no está en seguridad.db)
    public string? Servidor { get; set; }
    public int? Puerto { get; set; }
    public string BaseDatos { get; set; } = "KLKPOS_TIENDA";
    public string? Usuario { get; set; }
    public string? Clave { get; set; }
    public string? ConexionDirecta { get; set; }

    // Ruta a carpeta datos/ (seguridad.db + tiendas.key)
    public string? CarpetaDatos { get; set; }

    // Filtros específicos de reportes
    public string Caja { get; set; } = "";
    public string NoFiscal { get; set; } = "";       // "" todos | "0" fiscales | "1" no fiscales
    public string Credito { get; set; } = "";        // "" todos | "0" contado | "1" crédito
    public List<string> Localidades { get; } = new();
    public bool SoloRecepcion { get; set; }

    // Ejecución y salida
    public int TimeoutSeg { get; set; } = 120;
    public int LimiteFilas { get; set; } = 25;
    public int Paralelo { get; set; } = 6;
    public string? ExportarCsv { get; set; }
    public bool ListarTiendas { get; set; }
    public bool Menu { get; set; }
    public bool Ayuda { get; set; }

    public bool TieneConexionManual =>
        !string.IsNullOrWhiteSpace(ConexionDirecta) || !string.IsNullOrWhiteSpace(Servidor);

    public static Parametros Parsear(string[] args)
    {
        var p = new Parametros();
        if (args.Length == 0) { p.Menu = true; return p; }

        bool hastaExplicito = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Valor() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Falta el valor para {a}.");

            switch (a.ToLowerInvariant())
            {
                case "-m":
                case "--menu":
                    p.Menu = true;
                    break;
                case "-h":
                case "--ayuda":
                case "/?":
                    p.Ayuda = true;
                    break;
                case "--listar-tiendas":
                    p.ListarTiendas = true;
                    break;
                case "-r":
                case "--reporte":
                    p.Reporte = Valor()!.Trim().ToLowerInvariant();
                    break;
                case "-t":
                case "--tienda":
                case "--tiendas":
                    foreach (var s in Valor()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        p.Tiendas.Add(s);
                    break;
                case "--todas":
                    p.Todas = true;
                    break;
                case "-d":
                case "--desde":
                    p.Desde = ParseFecha(Valor()!);
                    break;
                case "-f":
                case "--hasta":
                    p.Hasta = ParseFecha(Valor()!);
                    hastaExplicito = true;
                    break;
                case "--servidor":
                    p.Servidor = Valor()!.Trim();
                    break;
                case "--puerto":
                    p.Puerto = int.Parse(Valor()!, CultureInfo.InvariantCulture);
                    break;
                case "--base":
                    p.BaseDatos = Valor()!.Trim();
                    break;
                case "--usuario":
                    p.Usuario = Valor()!.Trim();
                    break;
                case "--clave":
                    p.Clave = Valor();
                    break;
                case "--conexion":
                    p.ConexionDirecta = Valor();
                    break;
                case "--datos":
                    p.CarpetaDatos = Valor();
                    break;
                case "--caja":
                    p.Caja = Valor()!.Trim();
                    break;
                case "--no-fiscal":
                    p.NoFiscal = Valor()!.Trim();
                    break;
                case "--credito":
                    p.Credito = Valor()!.Trim();
                    break;
                case "--localidades":
                    foreach (var l in Valor()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        p.Localidades.Add(l);
                    break;
                case "--solo-recepcion":
                    p.SoloRecepcion = true;
                    break;
                case "--timeout":
                    p.TimeoutSeg = Math.Clamp(int.Parse(Valor()!, CultureInfo.InvariantCulture), 5, 600);
                    break;
                case "--limite":
                    p.LimiteFilas = Math.Max(0, int.Parse(Valor()!, CultureInfo.InvariantCulture));
                    break;
                case "--paralelo":
                    p.Paralelo = Math.Clamp(int.Parse(Valor()!, CultureInfo.InvariantCulture), 1, 20);
                    break;
                case "--csv":
                    p.ExportarCsv = Valor();
                    break;
                default:
                    throw new ArgumentException($"Parámetro desconocido: {a}. Use --ayuda para ver las opciones.");
            }
        }

        if (!hastaExplicito) p.Hasta = p.Desde;
        if (p.Hasta < p.Desde) throw new ArgumentException("La fecha --desde no puede ser posterior a --hasta.");
        if (string.IsNullOrEmpty(p.Clave)) p.Clave = Environment.GetEnvironmentVariable("PRUEBA_CLAVE");
        return p;
    }

    private static DateTime ParseFecha(string valor)
    {
        string v = valor.Trim().ToLowerInvariant();
        if (v == "hoy") return DateTime.Today;
        if (v == "ayer") return DateTime.Today.AddDays(-1);
        if (v == "mes") return new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (DateTime.TryParseExact(v, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        throw new ArgumentException($"Fecha inválida '{valor}'. Use yyyy-MM-dd, dd/MM/yyyy, 'hoy' o 'ayer'.");
    }

    public static void MostrarAyuda()
    {
        Console.WriteLine("""
VentasReportesApi.PruebasReportes — Ejecutor parametrizado de pruebas de reportes
================================================================================

Reportes disponibles (--reporte / -r):
  libroiva       Libro de ventas I.V.A. (con validaciones fiscales automáticas)
  cruce          Cruce automático: Resumen vs Diario vs Libro IVA
  auditoria      Auditoría de tienda(s): conexión, proc POS, reloj, seriales y NoFiscal
  ventas         Reporte Ventas POS (dbo.getVentasPosStore)
  resumen        Resumen de ventas por tienda
  diario         Ventas diarias
  categorias     Ventas por categoría (admite --caja, --no-fiscal, --credito)
  cierres        Cierres de caja, arqueos y ventas sin cierre
  listado        Listado detallado de documentos, renglones y pagos
  entradas       Entradas de mercancía (KLK_ENTRADAHDR)
  productos      Análisis de productos por mes
  tasa           Última tasa de cambio USD de la tienda
  compras        Compras + ODC (base central VAD10, requiere --conexion)
  ultimoscostos  Últimos costos de compra (base central VAD10, requiere --conexion)
  todo           Ejecuta un smoke test de los reportes principales de tienda

Selección de tiendas:
  --tienda TURMERO               Una tienda configurada en seguridad.db
  --tienda BOSQUE,CENTRO         Varias tiendas separadas por coma
  --todas                        Todas las tiendas activas en seguridad.db
  --listar-tiendas               Lista las tiendas disponibles en seguridad.db
  --servidor IP --puerto 59241 --base KLKPOS_TIENDA --usuario usr --clave pwd
                                 Conexión directa (también lee la clave de env PRUEBA_CLAVE)
  --conexion "Server=...;"       Cadena de conexión directa (útil para compras/VAD10)
  --datos "ruta\a\datos"         Carpeta con seguridad.db y tiendas.key (se autodetecta)

Fechas y filtros:
  --desde ayer|hoy|yyyy-MM-dd    Fecha inicial (por defecto: ayer)
  --hasta ayer|hoy|yyyy-MM-dd    Fecha final (por defecto: igual a --desde)
  --caja 000001                  Filtro de caja (categorias)
  --no-fiscal 0|1                0 = solo fiscales, 1 = solo no fiscales (categorias)
  --credito 0|1                  0 = solo contado, 1 = solo crédito (categorias)
  --localidades 14,17            Localidades de compras/ultimoscostos
  --solo-recepcion               Solo compras con recepción (ultimoscostos)

Salida:
  --limite 25                    Máximo de filas a mostrar en consola (0 = solo resumen)
  --csv reporte.csv              Exporta las filas resultantes a CSV (UTF-8 con BOM para Excel)
  --timeout 120                  Timeout SQL en segundos (por defecto: 120)
  --paralelo 6                   Consultas simultáneas al usar --todas (por defecto: 6)

Ejemplos:
  dotnet run -- -r libroiva -t TURMERO --desde ayer
  dotnet run -- -r libroiva --servidor 172.25.20.10 --puerto 59241 --usuario reportsRetail --clave "***" --desde 2026-11-19
  dotnet run -- -r resumen --todas --desde ayer --csv resumen-ayer.csv
  dotnet run -- -r todo -t TURMERO --desde 2026-11-19
""");
    }
}
