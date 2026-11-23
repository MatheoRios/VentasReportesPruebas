using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using VentasReportesApi;

namespace VentasReportesPruebas;

internal enum NivelEstado { Ok, Warn, Fail }

internal sealed record ResultadoEjecucion(
    string Tienda,
    string Reporte,
    NivelEstado Estado,
    long Ms,
    int Filas,
    string Resumen,
    string[] Encabezados,
    List<object?[]> Datos,
    List<string> Avisos)
{
    public bool Ok => Estado != NivelEstado.Fail;
}

internal static class EjecutorPruebas
{
    public static readonly string[] ReportesSmoke = { "resumen", "diario", "categorias", "cierres", "entradas", "libroiva", "tasa" };

    public static async Task<ResultadoEjecucion> EjecutarAsync(string tienda, string cadena, string reporte, Parametros p, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var avisos = new List<string>();
        try
        {
            switch (reporte)
            {
                case "auditoria":
                    return await EjecutarAuditoriaAsync(tienda, cadena, p, sw, ct);
                case "cruce":
                    return await EjecutarCruceAsync(tienda, cadena, p, sw, ct);
                case "ventas":
                {
                    var r = await ConsultaSucursal.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Filas.Count,
                        $"{r.Filas.Count:N0} filas · {r.Columnas.Count} columnas",
                        r.Columnas.Select(c => c.Nombre).ToArray(), r.Filas, avisos);
                }
                case "resumen":
                {
                    var r = await ConsultaResumen.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    var fila = new object?[] { tienda, r.Facturas, r.NotasCredito, r.VentaBruta, r.Devoluciones, r.VentaNeta, r.Unidades };
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, 1,
                        $"Fact={r.Facturas:N0}, NC={r.NotasCredito:N0}, Neta=${r.VentaNeta:N2}, Unid={r.Unidades:N2}",
                        new[] { "Tienda", "Facturas", "NotasCredito", "VentaBruta", "Devoluciones", "VentaNeta", "Unidades" },
                        new List<object?[]> { fila }, avisos);
                }
                case "diario":
                {
                    var r = await ConsultaDiaria.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    decimal neta = r.Sum(f => Dec(f[5]));
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Count,
                        $"{r.Count} días · VentaNeta=${neta:N2}",
                        new[] { "Fecha", "Facturas", "NotasCredito", "VentaBruta", "Devoluciones", "VentaNeta" },
                        r, avisos);
                }
                case "categorias":
                {
                    var r = await ConsultaCategorias.EjecutarAsync(cadena, p.Desde, p.Hasta, p.Caja, p.NoFiscal, p.Credito, p.TimeoutSeg, ct);
                    sw.Stop();
                    decimal baseUsd = r.Sum(f => Dec(f[6]));
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Count,
                        $"{r.Count:N0} artículos · BaseUSD=${baseUsd:N2}",
                        new[] { "Categoria", "Articulo", "Descripcion", "Unidad", "Existencia", "Cantidad", "BaseUsd", "IvaUsd", "CostoUsd", "BaseBs", "IvaBs", "CostoBs" },
                        r, avisos);
                }
                case "cierres":
                {
                    var r = await ConsultaCierres.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    var estado = NivelEstado.Ok;
                    if (r.SinCierre.Count > 0)
                    {
                        estado = NivelEstado.Warn;
                        avisos.Add($"Hay {r.SinCierre.Count} apertura(s) con ventas sin cierre X.");
                    }
                    var enc = new[] { "Nro", "Fecha", "Caja", "Apertura", "CodCajero", "Cajero", "Tasa", "Enviado", "EnviadoSTDA", "Total", "Total1", "Total2" };
                    var resumidas = r.Cierres.Select(f => f.Take(12).ToArray()).ToList();
                    return new ResultadoEjecucion(tienda, reporte, estado, sw.ElapsedMilliseconds, r.Cierres.Count,
                        $"{r.Cierres.Count} cierres · {r.Arqueos.Count} arqueos · {r.SinCierre.Count} sin cierre",
                        enc, resumidas, avisos);
                }
                case "listado":
                {
                    var r = await ConsultaListado.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Lineas.Count,
                        $"{r.Documentos.Count:N0} docs · {r.Lineas.Count:N0} líneas · {r.Pagos.Count:N0} pagos",
                        new[] { "DocIdx", "Articulo", "Descripcion", "Cantidad", "TotalUsd" },
                        r.Lineas.Select(f => f.Take(5).ToArray()).ToList(), avisos);
                }
                case "entradas":
                {
                    var r = await ConsultaEntradas.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Count,
                        $"{r.Count:N0} entradas de mercancía",
                        Enumerable.Range(0, r.Count > 0 ? r[0].Length : 0).Select(i => "Col" + i).ToArray(),
                        r, avisos);
                }
                case "libroiva":
                {
                    var r = await ConsultaLibroIva.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    avisos.AddRange(r.Avisos);
                    decimal netoBs = 0, netoUsd = 0, base16 = 0, iva16 = 0;
                    int factDocs = 0, ncDocs = 0, sinTasa = 0, sinSerial = 0, sinZ = 0;
                    foreach (var f in r.Filas)
                    {
                        bool nc = string.Equals(Str(f[0]), "NC", StringComparison.OrdinalIgnoreCase);
                        decimal s = nc ? -1m : 1m;
                        decimal ex = s * Dec(f[6]), b16 = s * Dec(f[7]), i16 = s * Dec(f[8]);
                        decimal b8 = s * Dec(f[9]), i8 = s * Dec(f[10]), b31 = s * Dec(f[11]), i31 = s * Dec(f[12]);
                        netoBs += ex + b16 + i16 + b8 + i8 + b31 + i31;
                        netoUsd += s * (Dec(f[17]) + Dec(f[18]) + Dec(f[19]));
                        base16 += b16; iva16 += i16;
                        int docs = Convert.ToInt32(f[14], CultureInfo.InvariantCulture);
                        if (nc) ncDocs += docs; else factDocs += docs;
                        if (Str(f[2]).StartsWith("CAJA ", StringComparison.OrdinalIgnoreCase)) sinSerial++;
                        if (!nc && string.IsNullOrWhiteSpace(Str(f[3]))) sinZ++;
                        if (f.Length > 20) sinTasa += Convert.ToInt32(f[20], CultureInfo.InvariantCulture);
                    }

                    // Validaciones automáticas de integridad fiscal
                    decimal ratio = base16 != 0 ? iva16 / base16 * 100m : 0m;
                    if (r.Filas.Count == 0)
                    {
                        string? diag = await DiagnosticarLibroIvaVacioAsync(cadena, p.Desde, p.Hasta, ct);
                        if (diag != null) avisos.Add(diag);
                    }
                    else
                    {
                        if (base16 > 0 && (ratio < 15.90m || ratio > 16.10m))
                            avisos.Add($"Alícuota IVA 16% fuera de rango esperado: {ratio:N2}% (Base16={base16:N2}, IVA16={iva16:N2}).");
                        if (sinTasa > 0) avisos.Add($"Hay {sinTasa:N0} documento(s) sin tasa USD (USD subestimado).");
                        if (sinSerial > 0) avisos.Add($"Hay {sinSerial} fila(s) sin SerialImpresora configurado (usando 'CAJA ...').");
                        if (sinZ > 0) avisos.Add($"Hay {sinZ} fila(s) de facturas sin número de reporte Z (NCorteZ vacío).");
                    }

                    var estado = avisos.Count > 0 ? NivelEstado.Warn : NivelEstado.Ok;
                    return new ResultadoEjecucion(tienda, reporte, estado, sw.ElapsedMilliseconds, r.Filas.Count,
                        $"{r.Filas.Count} filas ({factDocs:N0} fact, {ncDocs:N0} NC) · Neto={netoBs:N2} Bs (${netoUsd:N2}) · IVA16={ratio:N2}%",
                        new[] { "Tipo", "Fecha", "Serial", "Z", "Inicial", "Final", "Exento", "Base16", "Iva16", "Base8", "Iva8", "Base31", "Iva31", "Retenido", "Docs", "Afectada", "Orden", "ExentoUsd", "BaseUsd", "IvaUsd", "SinTasa" },
                        r.Filas, avisos);
                }
                case "productos":
                {
                    var r = await ConsultaProductosMes.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
                    sw.Stop();
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Meses.Count,
                        $"{r.Meses.Count:N0} registros mes · {r.Articulos.Count:N0} artículos · {r.Bloqueados.Count} bloqueados",
                        new[] { "Articulo", "Mes", "Cantidad", "Usd" }, r.Meses, avisos);
                }
                case "tasa":
                {
                    var r = await ConsultaTasa.UltimaAsync(cadena, ct);
                    sw.Stop();
                    if (!r.HasValue)
                        return new ResultadoEjecucion(tienda, reporte, NivelEstado.Warn, sw.ElapsedMilliseconds, 0, "Sin cierres con tasa",
                            new[] { "Tienda", "Tasa", "Fecha" }, new List<object?[]>(), new List<string> { "No se encontró TasaDeCambio > 0 en KLK_CIERRE_X." });
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, 1,
                        $"Tasa={r.Value.Tasa:N4} Bs/USD ({r.Value.Fecha:yyyy-MM-dd HH:mm})",
                        new[] { "Tienda", "Tasa", "Fecha" },
                        new List<object?[]> { new object?[] { tienda, r.Value.Tasa, r.Value.Fecha.ToString("yyyy-MM-dd HH:mm") } }, avisos);
                }
                case "compras":
                {
                    var r = await ConsultaCompras.EjecutarAsync(cadena, p.Desde, p.Hasta, p.Localidades, p.TimeoutSeg, ct);
                    sw.Stop();
                    if (!string.IsNullOrWhiteSpace(r.AvisoOdc)) avisos.Add(r.AvisoOdc);
                    return new ResultadoEjecucion(tienda, reporte, avisos.Count > 0 ? NivelEstado.Warn : NivelEstado.Ok,
                        sw.ElapsedMilliseconds, r.Documentos.Count,
                        $"{r.Documentos.Count:N0} facturas · {r.Lineas.Count:N0} líneas · {r.Ordenes.Count:N0} ODC",
                        new[] { "Documento", "Fecha", "CodProv", "Proveedor", "Rif", "FactProv", "Moneda", "Simbolo", "Factor", "Subtotal", "Descuento", "Impuesto", "Total", "Localidad" },
                        r.Documentos.Select(f => f.Take(14).ToArray()).ToList(), avisos);
                }
                case "ultimoscostos":
                {
                    var r = await ConsultaUltimosCostos.EjecutarAsync(cadena, p.Desde, p.Hasta, p.Localidades, p.TimeoutSeg, ct, p.SoloRecepcion);
                    sw.Stop();
                    return new ResultadoEjecucion(tienda, reporte, NivelEstado.Ok, sw.ElapsedMilliseconds, r.Count,
                        $"{r.Count:N0} productos con último costo",
                        new[] { "Codigo", "Descripcion", "Departamento", "Presentacion", "PorPeso", "Costo", "Fecha", "Documento", "CodProv", "Proveedor", "Localidad", "Moneda" },
                        r.Select(f => f.Take(12).ToArray()).ToList(), avisos);
                }
                default:
                    throw new ArgumentException($"Reporte '{reporte}' no reconocido. Use --ayuda para ver la lista.");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ResultadoEjecucion(tienda, reporte, NivelEstado.Fail, sw.ElapsedMilliseconds, 0,
                ConsultaSucursal.MensajeAmigable(ex), Array.Empty<string>(), new List<object?[]>(), avisos);
        }
    }

    // ------------------------------------------------------------------ Auditoría rápida de salud/configuración KLK
    private static async Task<ResultadoEjecucion> EjecutarAuditoriaAsync(string tienda, string cadena, Parametros p, Stopwatch sw, CancellationToken ct)
    {
        var avisos = new List<string>();
        await using var conn = new SqlConnection(cadena);
        await conn.OpenAsync(ct);
        long msConn = sw.ElapsedMilliseconds;

        await using var cmd = new SqlCommand(@"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
DECLARE @d DATETIME = @p1, @h DATETIME = DATEADD(DAY, 1, @p2);
SELECT
    CASE WHEN OBJECT_ID('dbo.getVentasPosStore') IS NULL THEN 0 ELSE 1 END AS TieneProc,
    DATEDIFF(MINUTE, GETDATE(), @ahoraLocal) AS DesfaseMin,
    (SELECT COUNT(*) FROM KLK_CAJA) AS TotalCajas,
    (SELECT COUNT(*) FROM KLK_CAJA WHERE LTRIM(RTRIM(ISNULL(SerialImpresora, N''))) NOT IN (N'', N'0')) AS CajasConSerial,
    (SELECT COUNT(*) FROM KLK_FACTURAHDR WHERE FechaFactura >= @d AND FechaFactura < @h AND EnEspera = 0
        AND ISNULL(CONVERT(NVARCHAR(10), NoFiscal), N'0') NOT IN (N'1', N'True', N'S')) AS FactFiscales,
    (SELECT COUNT(*) FROM KLK_FACTURAHDR WHERE FechaFactura >= @d AND FechaFactura < @h AND EnEspera = 0
        AND ISNULL(CONVERT(NVARCHAR(10), NoFiscal), N'0') IN (N'1', N'True', N'S')) AS FactNoFiscales,
    (SELECT COUNT(*) FROM KLK_FACTURAHDR WHERE FechaFactura >= @d AND FechaFactura < @h AND EnEspera = 0
        AND ISNULL(NumTicketFiscal, N'') = N'') AS SinTicket;", conn) { CommandTimeout = Math.Min(p.TimeoutSeg, 25) };
        cmd.Parameters.AddWithValue("@p1", p.Desde.Date);
        cmd.Parameters.AddWithValue("@p2", p.Hasta.Date);
        cmd.Parameters.AddWithValue("@ahoraLocal", DateTime.Now);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        sw.Stop();

        bool proc = rd.GetInt32(0) == 1;
        int desfase = rd.GetInt32(1);
        int cajas = rd.GetInt32(2), conSerial = rd.GetInt32(3);
        int fisc = rd.GetInt32(4), noFisc = rd.GetInt32(5), sinTkt = rd.GetInt32(6);

        if (!proc) avisos.Add("Falta el procedimiento dbo.getVentasPosStore.");
        if (Math.Abs(desfase) > 5) avisos.Add($"Reloj del servidor desfasado {desfase:+#;-#;0} min respecto a esta PC.");
        if (cajas > 0 && conSerial < cajas) avisos.Add($"{cajas - conSerial} de {cajas} caja(s) sin SerialImpresora.");
        if (noFisc > 0) avisos.Add($"Hay {noFisc:N0} factura(s) con NoFiscal = 1 en el período (fiscales={fisc:N0}).");
        if (sinTkt > 0) avisos.Add($"Hay {sinTkt:N0} factura(s) sin NumTicketFiscal en el período.");
        if (msConn > 2500) avisos.Add($"Conexión lenta ({msConn:N0} ms).");

        var estado = !proc ? NivelEstado.Fail : avisos.Count > 0 ? NivelEstado.Warn : NivelEstado.Ok;
        string relojTxt = Math.Abs(desfase) <= 5 ? "OK" : $"{desfase:+#;-#;0}m";
        string resumen = $"Conn={msConn:N0}ms | Proc={(proc ? "OK" : "FALTA")} | Reloj={relojTxt} | Seriales={conSerial}/{cajas} | Fisc={fisc:N0} | NoFisc={noFisc:N0}";
        var fila = new object?[] { tienda, msConn, proc ? "OK" : "FALTA", relojTxt, $"{conSerial}/{cajas}", fisc, noFisc, sinTkt, estado.ToString().ToUpperInvariant() };
        return new ResultadoEjecucion(tienda, "auditoria", estado, sw.ElapsedMilliseconds, 1, resumen,
            new[] { "Tienda", "ConnMs", "ProcPOS", "Reloj", "Seriales", "FactFiscales", "FactNoFiscales", "SinTicket", "Estado" },
            new List<object?[]> { fila }, avisos);
    }

    // ------------------------------------------------------------------ Cruce automático Resumen vs Diario vs Libro IVA
    private static async Task<ResultadoEjecucion> EjecutarCruceAsync(string tienda, string cadena, Parametros p, Stopwatch sw, CancellationToken ct)
    {
        var avisos = new List<string>();
        var tRes = ConsultaResumen.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
        var tDia = ConsultaDiaria.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
        var tIva = ConsultaLibroIva.EjecutarAsync(cadena, p.Desde, p.Hasta, p.TimeoutSeg, ct);
        await Task.WhenAll(tRes, tDia, tIva);
        sw.Stop();

        var res = tRes.Result;
        var dia = tDia.Result;
        var iva = tIva.Result;

        long diaFact = dia.Sum(f => Convert.ToInt64(f[1], CultureInfo.InvariantCulture));
        long diaNc = dia.Sum(f => Convert.ToInt64(f[2], CultureInfo.InvariantCulture));
        decimal diaNeta = dia.Sum(f => Dec(f[5]));

        long ivaFact = 0, ivaNc = 0;
        decimal ivaUsd = 0;
        foreach (var f in iva.Filas)
        {
            bool nc = string.Equals(Str(f[0]), "NC", StringComparison.OrdinalIgnoreCase);
            int docs = Convert.ToInt32(f[14], CultureInfo.InvariantCulture);
            if (nc) ivaNc += docs; else ivaFact += docs;
            ivaUsd += (nc ? -1m : 1m) * (Dec(f[17]) + Dec(f[18]) + Dec(f[19]));
        }

        if (res.Facturas != diaFact || res.NotasCredito != diaNc)
            avisos.Add($"Resumen vs Diario: difieren documentos (Resumen {res.Facturas}/{res.NotasCredito} vs Diario {diaFact}/{diaNc}).");
        if (Math.Abs(res.VentaNeta - diaNeta) > 0.05m)
            avisos.Add($"Resumen vs Diario: difiere VentaNeta USD (Resumen ${res.VentaNeta:N2} vs Diario ${diaNeta:N2}, dif ${(res.VentaNeta - diaNeta):N2}).");

        if (res.Facturas != ivaFact || res.NotasCredito != ivaNc)
            avisos.Add($"Resumen vs LibroIVA: difieren documentos (Resumen {res.Facturas}/{res.NotasCredito} vs LibroIVA {ivaFact}/{ivaNc}; revisar NoFiscal o SinTicket).");
        decimal difUsdIva = res.VentaNeta - ivaUsd;
        if (Math.Abs(difUsdIva) > 2.00m && (res.Facturas > 0 || ivaFact > 0))
            avisos.Add($"Resumen vs LibroIVA: diferencia en USD de ${difUsdIva:N2} (Resumen ${res.VentaNeta:N2} vs LibroIVA ${ivaUsd:N2}; puede deberse a tasa o NoFiscal).");

        var filas = new List<object?[]>
        {
            new object?[] { "Resumen",   res.Facturas, res.NotasCredito, res.VentaNeta, "Base KLK_FACTURAHDR + NCREDITOHDR" },
            new object?[] { "Diario",    diaFact,      diaNc,            diaNeta,       $"{dia.Count} día(s)" },
            new object?[] { "Libro IVA", ivaFact,      ivaNc,            ivaUsd,        $"{iva.Filas.Count} fila(s) fiscales" }
        };

        var estado = avisos.Count > 0 ? NivelEstado.Warn : NivelEstado.Ok;
        string resumen = avisos.Count == 0
            ? $"CUADRA: {res.Facturas:N0} fact, {res.NotasCredito:N0} NC · Resumen=${res.VentaNeta:N2} · Diario=${diaNeta:N2} · LibroIVA=${ivaUsd:N2}"
            : $"{avisos.Count} diferencia(s) · Resumen=${res.VentaNeta:N2} ({res.Facturas}) · Diario=${diaNeta:N2} ({diaFact}) · LibroIVA=${ivaUsd:N2} ({ivaFact})";

        return new ResultadoEjecucion(tienda, "cruce", estado, sw.ElapsedMilliseconds, filas.Count, resumen,
            new[] { "Reporte", "Facturas", "NotasCredito", "VentaNetaUsd", "Detalle" }, filas, avisos);
    }

    private static async Task<string?> DiagnosticarLibroIvaVacioAsync(string cadena, DateTime desde, DateTime hasta, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(cadena);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT COUNT(*) FROM KLK_FACTURAHDR
WHERE FechaFactura >= @p1 AND FechaFactura < DATEADD(DAY, 1, @p2)
  AND EnEspera = 0
  AND ISNULL(CONVERT(NVARCHAR(10), NoFiscal), N'0') IN (N'1', N'True', N'S')
  AND ISNULL(NumTicketFiscal, N'') <> N'';", conn) { CommandTimeout = 15 };
            cmd.Parameters.AddWithValue("@p1", desde.Date);
            cmd.Parameters.AddWithValue("@p2", hasta.Date);
            int noFiscales = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (noFiscales > 0)
                return $"DIAGNÓSTICO: hay {noFiscales:N0} facturas con NumTicketFiscal pero marcadas NoFiscal = 1 (revisar configuración KLK de esta tienda).";
        }
        catch { }
        return null;
    }

    public static void ImprimirTabla(string[] encabezados, List<object?[]> filas, int limite)
    {
        if (limite <= 0 || filas.Count == 0 || encabezados.Length == 0) return;
        int nCols = Math.Min(encabezados.Length, filas[0].Length);
        var muestra = filas.Take(limite).ToList();
        var anchos = new int[nCols];
        for (int c = 0; c < nCols; c++)
        {
            anchos[c] = encabezados[c].Length;
            foreach (var f in muestra) anchos[c] = Math.Max(anchos[c], Math.Min(36, Formatear(f[c]).Length));
        }

        Console.WriteLine();
        Console.WriteLine(string.Join(" | ", Enumerable.Range(0, nCols).Select(c => encabezados[c].PadRight(anchos[c]))));
        Console.WriteLine(new string('-', anchos.Sum() + Math.Max(0, nCols - 1) * 3));
        foreach (var f in muestra)
        {
            Console.WriteLine(string.Join(" | ", Enumerable.Range(0, nCols).Select(c =>
            {
                string s = Formatear(f[c]);
                if (s.Length > 36) s = s[..35] + "…";
                return f[c] is decimal or int or long or double ? s.PadLeft(anchos[c]) : s.PadRight(anchos[c]);
            })));
        }
        if (filas.Count > limite)
            Console.WriteLine($"... ({filas.Count - limite:N0} filas adicionales; use --limite o --csv para ver todas)");
    }

    public static void ExportarCsv(string ruta, IEnumerable<ResultadoEjecucion> resultados)
    {
        var lista = resultados.Where(r => r.Ok && r.Encabezados.Length > 0).ToList();
        if (lista.Count == 0) return;
        var sb = new StringBuilder();
        var enc = new[] { "Tienda" }.Concat(lista[0].Encabezados);
        sb.AppendLine(string.Join(";", enc.Select(EscCsv)));
        foreach (var r in lista)
            foreach (var f in r.Datos)
                sb.AppendLine(string.Join(";", new object?[] { r.Tienda }.Concat(f).Select(v => EscCsv(FormatearCsv(v)))));
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"\nCSV exportado: {Path.GetFullPath(ruta)}");
    }

    private static string EscCsv(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static string Formatear(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("N2", CultureInfo.CurrentCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    private static string FormatearCsv(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("0.######", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    private static decimal Dec(object? o) => o == null ? 0m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);
    private static string Str(object? o) => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
}
