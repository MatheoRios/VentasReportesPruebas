using System.Globalization;
using VentasReportesApi;

namespace VentasReportesPruebas;

internal static class MenuInteractivo
{
    private static readonly (string Clave, string Nombre)[] Reportes =
    {
        ("libroiva",      "Libro de ventas I.V.A. (con validaciones)"),
        ("cruce",         "Cruce Resumen vs Diario vs Libro IVA"),
        ("auditoria",     "Auditoría de red (salud, reloj, serial, NoFiscal)"),
        ("resumen",       "Resumen de ventas por tienda"),
        ("diario",        "Ventas diarias"),
        ("categorias",    "Ventas por categoría"),
        ("cierres",       "Cierres de caja y arqueos"),
        ("ventas",        "Reporte Ventas POS (getVentasPosStore)"),
        ("listado",       "Listado detallado de ventas"),
        ("entradas",      "Entradas de mercancía"),
        ("productos",     "Análisis de productos por mes"),
        ("tasa",          "Última tasa USD"),
        ("todo",          "Smoke test (todos los reportes principales)"),
        ("compras",       "Compras + ODC (base central VAD10)"),
        ("ultimoscostos", "Últimos costos de compra (base central VAD10)")
    };

    public static async Task<int> EjecutarAsync(Func<string?> resolverDatos, Func<Parametros, Task<int>> correr)
    {
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================");
            Console.WriteLine("   VentasReportesPruebas  ·  Menú interactivo de pruebas");
            Console.WriteLine("============================================================");
            Console.ResetColor();

            for (int i = 0; i < Reportes.Length; i++)
                Console.WriteLine($"  {i + 1,2}) {Reportes[i].Nombre,-45} [{Reportes[i].Clave}]");
            Console.WriteLine("   0) Salir");

            string? op = Pedir("\nElija un reporte (número o clave)", "1");
            if (op == null || op == "0" || op.Equals("salir", StringComparison.OrdinalIgnoreCase))
                return 0;

            string reporte;
            if (int.TryParse(op, out int idx) && idx >= 1 && idx <= Reportes.Length)
                reporte = Reportes[idx - 1].Clave;
            else if (Reportes.Any(r => r.Clave.Equals(op, StringComparison.OrdinalIgnoreCase)))
                reporte = op.ToLowerInvariant();
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Opción no válida.");
                Console.ResetColor();
                continue;
            }

            var p = new Parametros { Reporte = reporte };

            // Destino (base central o tiendas)
            if (reporte is "compras" or "ultimoscostos")
            {
                p.ConexionDirecta = Pedir("Cadena de conexión SQL (base VAD10)", null);
                if (string.IsNullOrWhiteSpace(p.ConexionDirecta)) continue;
                string? locs = Pedir("Localidades separadas por coma (Enter = todas)", "");
                if (!string.IsNullOrWhiteSpace(locs))
                    foreach (var l in locs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        p.Localidades.Add(l);
                if (reporte == "ultimoscostos")
                {
                    string? sr = Pedir("¿Solo compras con recepción? (s/N)", "n");
                    p.SoloRecepcion = sr != null && sr.StartsWith("s", StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                if (!SeleccionarTiendas(p, resolverDatos)) continue;
            }

            // Fechas (excepto tasa)
            if (reporte != "tasa")
            {
                p.Desde = PedirFecha("Fecha DESDE (yyyy-MM-dd, 'ayer', 'hoy')", DateTime.Today.AddDays(-1));
                p.Hasta = PedirFecha("Fecha HASTA (Enter = igual a Desde)", p.Desde);
                if (p.Hasta < p.Desde)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("La fecha Hasta no puede ser anterior a Desde.");
                    Console.ResetColor();
                    continue;
                }
            }

            if (reporte == "categorias")
            {
                p.Caja = Pedir("Filtro caja (Enter = todas)", "") ?? "";
                p.NoFiscal = Pedir("NoFiscal: 0=fiscales, 1=no fiscales, Enter=todos", "") ?? "";
                p.Credito = Pedir("Crédito: 0=contado, 1=crédito, Enter=todos", "") ?? "";
            }

            if (reporte != "todo")
            {
                string? csv = Pedir("Exportar CSV (nombre de archivo o Enter para omitir)", "");
                if (!string.IsNullOrWhiteSpace(csv)) p.ExportarCsv = csv.Trim();
            }

            Console.WriteLine();
            await correr(p);

            string? otra = Pedir("\n¿Ejecutar otra prueba? (S/n)", "s");
            if (otra != null && otra.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                return 0;
        }
    }

    private static bool SeleccionarTiendas(Parametros p, Func<string?> resolverDatos)
    {
        string? datos = resolverDatos();
        var activas = new List<Tienda>();
        if (datos != null)
        {
            try
            {
                var db = new Almacen(Path.Combine(datos, "seguridad.db"));
                activas = db.ListarTiendas().Where(t => t.Activa).ToList();
            }
            catch { }
        }

        Console.WriteLine("\nTiendas disponibles:");
        for (int i = 0; i < activas.Count; i++)
        {
            Console.Write($"  {i + 1,2}) {activas[i].Nombre,-18}");
            if ((i + 1) % 3 == 0 || i == activas.Count - 1) Console.WriteLine();
        }
        Console.WriteLine("   T) TODAS las tiendas activas");
        Console.WriteLine("   M) Conexión manual (IP, puerto, usuario y clave)");

        string? sel = Pedir("Seleccione tienda(s) (ej. 21 · 4,21 · TURMERO · T · M)", activas.Count > 0 ? "1" : "M");
        if (string.IsNullOrWhiteSpace(sel)) return false;

        if (sel.Equals("T", StringComparison.OrdinalIgnoreCase))
        {
            p.Todas = true;
            return true;
        }

        if (sel.Equals("M", StringComparison.OrdinalIgnoreCase))
        {
            p.Servidor = Pedir("Servidor / IP (ej. 172.25.20.10)", null);
            if (string.IsNullOrWhiteSpace(p.Servidor)) return false;
            string? puertoTxt = Pedir("Puerto (Enter = por defecto)", "59241");
            if (!string.IsNullOrWhiteSpace(puertoTxt) && int.TryParse(puertoTxt, out int pt)) p.Puerto = pt;
            p.BaseDatos = Pedir("Base de datos", "KLKPOS_TIENDA") ?? "KLKPOS_TIENDA";
            p.Usuario = Pedir("Usuario SQL", "reportsRetail");
            p.Clave = PedirClave("Clave SQL: ");
            if (string.IsNullOrWhiteSpace(p.Usuario) || string.IsNullOrEmpty(p.Clave))
            {
                Console.WriteLine("Debe indicar usuario y clave.");
                return false;
            }
            return true;
        }

        foreach (var item in sel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(item, out int n) && n >= 1 && n <= activas.Count)
                p.Tiendas.Add(activas[n - 1].Nombre);
            else
                p.Tiendas.Add(item);
        }
        return p.Tiendas.Count > 0;
    }

    private static DateTime PedirFecha(string etiqueta, DateTime defecto)
    {
        while (true)
        {
            string? v = Pedir(etiqueta, defecto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(v)) return defecto;
            string l = v.Trim().ToLowerInvariant();
            if (l == "hoy") return DateTime.Today;
            if (l == "ayer") return DateTime.Today.AddDays(-1);
            if (DateTime.TryParseExact(l, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.Date;
            Console.WriteLine("  Fecha inválida. Use yyyy-MM-dd, dd/MM/yyyy, 'ayer' o 'hoy'.");
        }
    }

    private static string? Pedir(string etiqueta, string? defecto)
    {
        Console.Write(string.IsNullOrEmpty(defecto) ? $"{etiqueta}: " : $"{etiqueta} [{defecto}]: ");
        string? linea = Console.ReadLine();
        if (linea == null) return null;
        linea = linea.Trim();
        return linea.Length == 0 ? defecto : linea;
    }

    private static string PedirClave(string titulo)
    {
        Console.Write(titulo);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); } }
            else if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); }
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
