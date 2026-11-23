using System.Collections.Concurrent;
using VentasReportesApi;
using VentasReportesPruebas;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Parametros inicial;
try { inicial = Parametros.Parsear(args); }
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Error en parámetros: " + ex.Message);
    Console.ResetColor();
    return 1;
}

if (inicial.Ayuda)
{
    Parametros.MostrarAyuda();
    return 0;
}

string? ResolverDatos(string? carpetaExplicita)
{
    if (!string.IsNullOrWhiteSpace(carpetaExplicita)) return carpetaExplicita;
    string dir = AppContext.BaseDirectory;
    for (int i = 0; i < 6; i++)
    {
        foreach (var cand in new[]
        {
            Path.Combine(dir, "datos"),
            Path.Combine(dir, "bin", "Debug", "net8.0", "datos"),
            Path.Combine(dir, "VentasReportesApi", "datos"),
            Path.Combine(dir, "VentasReportesApi", "bin", "Debug", "net8.0", "datos")
        })
            if (File.Exists(Path.Combine(cand, "seguridad.db"))) return cand;
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }
    return null;
}

async Task<int> CorrerAsync(Parametros p)
{
    List<(string Nombre, string Cadena)> objetivos = new();

    if (p.ListarTiendas)
    {
        string? datos = ResolverDatos(p.CarpetaDatos);
        if (datos == null) { Console.WriteLine("No se encontró datos\\seguridad.db. Indique la ruta con --datos."); return 1; }
        var db = new Almacen(Path.Combine(datos, "seguridad.db"));
        var lista = db.ListarTiendas();
        Console.WriteLine($"Tiendas en {datos} ({lista.Count}):");
        foreach (var t in lista)
            Console.WriteLine($"  {(t.Activa ? "[ACTIVA]  " : "[INACTIVA]")} {t.Nombre,-20} {t.Servidor}{(t.Puerto.HasValue ? "," + t.Puerto : "")} / {t.BaseDatos}");
        return 0;
    }

    if (p.TieneConexionManual)
    {
        string cadena;
        string etiqueta;
        if (!string.IsNullOrWhiteSpace(p.ConexionDirecta))
        {
            cadena = p.ConexionDirecta;
            etiqueta = p.Tiendas.FirstOrDefault() ?? "CONEXION_DIRECTA";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(p.Usuario) || string.IsNullOrEmpty(p.Clave))
            {
                Console.WriteLine("Al usar --servidor indique también --usuario y --clave (o variable de entorno PRUEBA_CLAVE).");
                return 1;
            }
            cadena = Conexiones.Cadena(p.Servidor!, p.Puerto, p.BaseDatos, p.Usuario, p.Clave, Math.Min(p.TimeoutSeg, 30), reintentos: 0);
            etiqueta = p.Tiendas.FirstOrDefault() ?? $"{p.Servidor}{(p.Puerto.HasValue ? ":" + p.Puerto : "")}";
        }
        objetivos.Add((etiqueta, cadena));
    }
    else if (p.Reporte is "compras" or "ultimoscostos")
    {
        Console.WriteLine($"El reporte '{p.Reporte}' consulta la base central (VAD10). Pase la cadena con --conexion \"Server=...;\"");
        return 1;
    }
    else
    {
        string? datos = ResolverDatos(p.CarpetaDatos);
        if (datos == null) { Console.WriteLine("No se encontró datos\\seguridad.db. Use --datos o --servidor."); return 1; }
        var db = new Almacen(Path.Combine(datos, "seguridad.db"));
        var cif = new Cifrado(Path.Combine(datos, "tiendas.key"));
        var todas = db.ListarTiendas().Where(t => t.Activa).ToList();

        if (p.Todas)
        {
            foreach (var t in todas)
                objetivos.Add((t.Nombre, Conexiones.Cadena(t.Servidor, t.Puerto, t.BaseDatos, t.Usuario, cif.Descifrar(t.ClaveCifrada), Math.Min(p.TimeoutSeg, 20), reintentos: 0)));
        }
        else if (p.Tiendas.Count > 0)
        {
            foreach (var nombre in p.Tiendas)
            {
                var t = todas.FirstOrDefault(x => x.Nombre.Equals(nombre, StringComparison.CurrentCultureIgnoreCase));
                if (t == null) { Console.WriteLine($"Tienda no encontrada o inactiva: '{nombre}'. Use --listar-tiendas."); return 1; }
                objetivos.Add((t.Nombre, Conexiones.Cadena(t.Servidor, t.Puerto, t.BaseDatos, t.Usuario, cif.Descifrar(t.ClaveCifrada), Math.Min(p.TimeoutSeg, 30), reintentos: 0)));
            }
        }
        else
        {
            Console.WriteLine("Indique --tienda NOMBRE, --todas o --servidor IP. Use --ayuda para ver ejemplos.");
            return 1;
        }
    }

    Console.WriteLine($"Reporte: {p.Reporte}  |  Período: {p.Desde:yyyy-MM-dd} → {p.Hasta:yyyy-MM-dd}  |  Objetivos: {objetivos.Count}");

    if (p.Reporte == "todo")
    {
        int fallos = 0;
        foreach (var obj in objetivos)
        {
            Console.WriteLine($"\n=== Smoke test: {obj.Nombre} ===");
            foreach (var rep in EjecutorPruebas.ReportesSmoke)
            {
                var r = await EjecutorPruebas.EjecutarAsync(obj.Nombre, obj.Cadena, rep, p, CancellationToken.None);
                Console.Write("  ");
                ImprimirEtiqueta(r.Estado);
                Console.WriteLine($"{rep,-12} {r.Ms,6:N0} ms  |  {r.Resumen}");
                foreach (var av in r.Avisos)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("         ! " + av);
                    Console.ResetColor();
                }
                if (r.Estado == NivelEstado.Fail) fallos++;
            }
        }
        return fallos == 0 ? 0 : 1;
    }

    var bolsa = new ConcurrentBag<ResultadoEjecucion>();
    using var sem = new SemaphoreSlim(p.Paralelo);

    await Task.WhenAll(objetivos.Select(async obj =>
    {
        await sem.WaitAsync();
        try { bolsa.Add(await EjecutorPruebas.EjecutarAsync(obj.Nombre, obj.Cadena, p.Reporte, p, CancellationToken.None)); }
        finally { sem.Release(); }
    }));

    var ordenados = bolsa.OrderBy(r => r.Tienda, StringComparer.CurrentCultureIgnoreCase).ToList();

    Console.WriteLine();
    foreach (var r in ordenados)
    {
        ImprimirEtiqueta(r.Estado);
        Console.WriteLine($"{r.Tienda,-22} ({r.Ms,5:N0} ms)  {r.Resumen}");
        foreach (var av in r.Avisos)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("       ! " + av);
            Console.ResetColor();
        }
    }

    if (ordenados.Count == 1 && ordenados[0].Ok)
        EjecutorPruebas.ImprimirTabla(ordenados[0].Encabezados, ordenados[0].Datos, p.LimiteFilas);

    if (!string.IsNullOrWhiteSpace(p.ExportarCsv))
        EjecutorPruebas.ExportarCsv(p.ExportarCsv, ordenados);

    int ok = ordenados.Count(r => r.Estado == NivelEstado.Ok);
    int warn = ordenados.Count(r => r.Estado == NivelEstado.Warn);
    int fail = ordenados.Count(r => r.Estado == NivelEstado.Fail);
    Console.WriteLine($"\nFinalizado: {ok} OK, {warn} con alertas (WARN), {fail} con error (FAIL).");
    return fail == 0 ? 0 : 1;
}

static void ImprimirEtiqueta(NivelEstado estado)
{
    Console.ForegroundColor = estado switch
    {
        NivelEstado.Ok => ConsoleColor.Green,
        NivelEstado.Warn => ConsoleColor.Yellow,
        _ => ConsoleColor.Red
    };
    Console.Write(estado switch
    {
        NivelEstado.Ok => "[OK]   ",
        NivelEstado.Warn => "[WARN] ",
        _ => "[FAIL] "
    });
    Console.ResetColor();
}

if (inicial.Menu)
    return await MenuInteractivo.EjecutarAsync(() => ResolverDatos(inicial.CarpetaDatos), CorrerAsync);

return await CorrerAsync(inicial);
