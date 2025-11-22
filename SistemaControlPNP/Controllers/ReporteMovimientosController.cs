using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ControlPNP.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPNP.Controllers
{
    [Authorize]
    [Route("Reportes/Movimientos")]
    public class ReporteMovimientosController : Controller
    {
        private readonly ControlDBContext _db;
        public ReporteMovimientosController(ControlDBContext db) => _db = db;

        // ===== Helpers =====
        private static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static (DateTime? d1Utc, DateTime? d2Utc) ParseDateRangeLocalToUtc(string? desde, string? hasta)
        {
            var tz = GetLimaTz();
            DateTime? d1 = null, d2 = null;

            if (!string.IsNullOrWhiteSpace(desde) &&
                DateTime.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x1))
            { x1 = DateTime.SpecifyKind(x1, DateTimeKind.Unspecified); d1 = TimeZoneInfo.ConvertTimeToUtc(x1, tz); }

            if (!string.IsNullOrWhiteSpace(hasta) &&
                DateTime.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x2))
            { x2 = DateTime.SpecifyKind(x2.AddDays(1), DateTimeKind.Unspecified); d2 = TimeZoneInfo.ConvertTimeToUtc(x2, tz); }

            return (d1, d2);
        }

        private static int Edad(DateTime fechaNacLocal, DateTime hoyLocal)
        {
            var e = hoyLocal.Year - fechaNacLocal.Year;
            if (hoyLocal.Date < fechaNacLocal.Date.AddYears(e)) e--;
            return e;
        }

        private static (string Fecha, string Hora, string IsoLocal) Fmt(DateTime utc)
        {
            var tz = GetLimaTz();
            var l = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            return (l.ToString("dd/MM/yyyy"), l.ToString("HH:mm"), l.ToString("yyyy-MM-ddTHH:mm:ss"));
        }

        // ===== VISTA =====
        [HttpGet("")]
        public IActionResult Index() => View("~/Views/Reportes/Movimientos.cshtml");

        // ===== JSON (respeta filtros, arrays para combos) =====
        [HttpGet("Json")]
        public async Task<IActionResult> Json(
            string? desde = null, string? hasta = null,
            string? sexo = null, int? edadMin = null, int? edadMax = null,
            string? estado = null,                  // "", "Pendiente", "Retornado"
            int? durMin = null, int? durMax = null, // minutos
            string[]? unidad = null, string[]? autoriza = null, string[]? motivo = null, string[]? destino = null,
            string? q = null,
            int take = 50000)
        {
            try
            {
                var tz = GetLimaTz();
                (DateTime? d1Utc, DateTime? d2Utc) = ParseDateRangeLocalToUtc(desde, hasta);

                var qMov = _db.Movimiento
                    .AsNoTracking()
                    .Include(m => m.Personal)
                    .AsQueryable();

                if (d1Utc.HasValue) qMov = qMov.Where(m => m.FechaSalida >= d1Utc.Value);
                if (d2Utc.HasValue) qMov = qMov.Where(m => m.FechaSalida < d2Utc.Value);

                if (!string.IsNullOrWhiteSpace(sexo))
                {
                    var sx = sexo.Trim().ToUpperInvariant();
                    qMov = qMov.Where(m => m.Personal != null && m.Personal.Sexo == sx);
                }

                if (!string.IsNullOrWhiteSpace(estado))
                {
                    if (estado.Equals("Pendiente", StringComparison.OrdinalIgnoreCase))
                        qMov = qMov.Where(m => m.FechaRetorno == null);
                    else if (estado.Equals("Retornado", StringComparison.OrdinalIgnoreCase))
                        qMov = qMov.Where(m => m.FechaRetorno != null);
                }

                // ===== Filtros múltiples (IN) =====
                if (unidad is { Length: > 0 })
                {
                    var set = unidad.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                    if (set.Length > 0)
                        qMov = qMov.Where(m => m.Personal != null && set.Contains(m.Personal.Unidad ?? ""));
                }
                if (autoriza is { Length: > 0 })
                {
                    var set = autoriza.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                    if (set.Length > 0)
                        qMov = qMov.Where(m => set.Contains(m.Autoriza ?? ""));
                }
                if (motivo is { Length: > 0 })
                {
                    var set = motivo.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                    if (set.Length > 0)
                        qMov = qMov.Where(m => set.Contains(m.Motivo ?? ""));
                }
                if (destino is { Length: > 0 })
                {
                    var set = destino.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                    if (set.Length > 0)
                        qMov = qMov.Where(m => set.Contains(m.Destino ?? ""));
                }

                // Búsqueda libre opcional en servidor
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    qMov = qMov.Where(m =>
                           (m.Personal != null && (
                                (m.Personal.Apellidos ?? "").Contains(s) ||
                                (m.Personal.Nombres ?? "").Contains(s) ||
                                (m.Personal.NumeroCip ?? "").Contains(s) ||
                                (m.Personal.Unidad ?? "").Contains(s)))
                        || (m.Motivo ?? "").Contains(s)
                        || (m.Autoriza ?? "").Contains(s)
                        || (m.Destino ?? "").Contains(s));
                }

                var list = await qMov
                    .OrderByDescending(m => m.FechaSalida)
                    .Take(Math.Clamp(take, 1000, 200000))
                    .ToListAsync();

                var hoyLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

                var rows = list.Select(m =>
                {
                    var p = m.Personal!;
                    int? edad = null;
                    if (p.FechaNacimiento != default) edad = Edad(p.FechaNacimiento, hoyLocal);

                    var (fS, hS, isoS) = Fmt(m.FechaSalida);
                    string fR = "", hR = "", isoR = "";
                    int? dur = null;

                    if (m.FechaRetorno.HasValue)
                    {
                        (fR, hR, isoR) = Fmt(m.FechaRetorno.Value);
                        dur = (int)Math.Round((m.FechaRetorno.Value - m.FechaSalida).TotalMinutes);
                        if (dur < 0) dur = 0;
                    }

                    var est = m.FechaRetorno.HasValue ? "Retornado" : "Pendiente";

                    return new
                    {
                        fecha = fS,
                        horaSalida = hS,
                        horaRetorno = hR,
                        salidaIso = isoS,
                        retornoIso = isoR,
                        duracionMin = dur,
                        estado = est,

                        cip = p.NumeroCip,
                        apellidos = p.Apellidos,
                        nombres = p.Nombres,
                        sexo = p.Sexo,
                        edad,

                        unidad = p.Unidad,
                        motivo = m.Motivo,
                        autoriza = m.Autoriza,
                        destino = string.IsNullOrWhiteSpace(m.Destino) ? "" : m.Destino
                    };
                }).ToList();

                // Filtro por edad y duración en memoria (después de cálculo)
                if (edadMin.HasValue) rows = rows.Where(r => r.edad.HasValue && r.edad.Value >= edadMin.Value).ToList();
                if (edadMax.HasValue) rows = rows.Where(r => r.edad.HasValue && r.edad.Value <= edadMax.Value).ToList();

                if (durMin.HasValue) rows = rows.Where(r => (r.duracionMin ?? int.MaxValue) >= durMin.Value).ToList();
                if (durMax.HasValue) rows = rows.Where(r => (r.duracionMin ?? int.MaxValue) <= durMax.Value).ToList();

                return Json(rows);
            }
            catch (Exception ex)
            {
                return Json(new { error = true, message = ex.Message });
            }
        }

        // ===== CSV (respeta TODOS los filtros, incluyendo arrays) =====
        [HttpGet("ExportCsv")]
        public async Task<IActionResult> ExportCsv(
            string? desde = null, string? hasta = null,
            string? sexo = null, int? edadMin = null, int? edadMax = null,
            string? estado = null,
            int? durMin = null, int? durMax = null,
            string[]? unidad = null, string[]? autoriza = null, string[]? motivo = null, string[]? destino = null,
            string? q = null)
        {
            var tz = GetLimaTz();
            (DateTime? d1Utc, DateTime? d2Utc) = ParseDateRangeLocalToUtc(desde, hasta);

            var qMov = _db.Movimiento.AsNoTracking().Include(m => m.Personal).AsQueryable();
            if (d1Utc.HasValue) qMov = qMov.Where(m => m.FechaSalida >= d1Utc.Value);
            if (d2Utc.HasValue) qMov = qMov.Where(m => m.FechaSalida < d2Utc.Value);

            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var sx = sexo.Trim().ToUpperInvariant();
                qMov = qMov.Where(m => m.Personal != null && m.Personal.Sexo == sx);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                if (estado.Equals("Pendiente", StringComparison.OrdinalIgnoreCase))
                    qMov = qMov.Where(m => m.FechaRetorno == null);
                else if (estado.Equals("Retornado", StringComparison.OrdinalIgnoreCase))
                    qMov = qMov.Where(m => m.FechaRetorno != null);
            }

            if (unidad is { Length: > 0 })
            {
                var set = unidad.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (set.Length > 0)
                    qMov = qMov.Where(m => m.Personal != null && set.Contains(m.Personal.Unidad ?? ""));
            }
            if (autoriza is { Length: > 0 })
            {
                var set = autoriza.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (set.Length > 0)
                    qMov = qMov.Where(m => set.Contains(m.Autoriza ?? ""));
            }
            if (motivo is { Length: > 0 })
            {
                var set = motivo.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (set.Length > 0)
                    qMov = qMov.Where(m => set.Contains(m.Motivo ?? ""));
            }
            if (destino is { Length: > 0 })
            {
                var set = destino.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (set.Length > 0)
                    qMov = qMov.Where(m => set.Contains(m.Destino ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                qMov = qMov.Where(m =>
                       (m.Personal != null && (
                            (m.Personal.Apellidos ?? "").Contains(s) ||
                            (m.Personal.Nombres ?? "").Contains(s) ||
                            (m.Personal.NumeroCip ?? "").Contains(s) ||
                            (m.Personal.Unidad ?? "").Contains(s)))
                    || (m.Motivo ?? "").Contains(s)
                    || (m.Autoriza ?? "").Contains(s)
                    || (m.Destino ?? "").Contains(s));
            }

            var data = await qMov.OrderByDescending(m => m.FechaSalida).Take(200000).ToListAsync();
            var hoyLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("Fecha;HoraSalida;FechaRetorno;HoraRetorno;DuracionMin;Estado;CIP;Apellidos;Nombres;Sexo;Edad;Unidad;Motivo;Autoriza;Destino");

            foreach (var m in data)
            {
                var p = m.Personal!;
                int? edad = null;
                if (p.FechaNacimiento != default) edad = Edad(p.FechaNacimiento, hoyLocal);

                (string fS, string hS, _) = Fmt(m.FechaSalida);
                string fR = "", hR = ""; int? dur = null;
                if (m.FechaRetorno.HasValue)
                {
                    (fR, hR, _) = Fmt(m.FechaRetorno.Value);
                    dur = (int)Math.Round((m.FechaRetorno.Value - m.FechaSalida).TotalMinutes);
                    if (dur < 0) dur = 0;
                }
                var est = m.FechaRetorno.HasValue ? "Retornado" : "Pendiente";

                if (durMin.HasValue && (dur ?? int.MaxValue) < durMin.Value) continue;
                if (durMax.HasValue && (dur ?? int.MaxValue) > durMax.Value) continue;

                string Esc(string? s) => (s ?? "").Replace(";", ",").Replace(Environment.NewLine, " ").Trim();

                sb.AppendLine($"{fS};{hS};{fR};{hR};{dur};{est};{Esc(p.NumeroCip)};{Esc(p.Apellidos)};{Esc(p.Nombres)};{Esc(p.Sexo)};{edad};{Esc(p.Unidad)};{Esc(m.Motivo)};{Esc(m.Autoriza)};{Esc(m.Destino)}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", $"reporte_movimientos_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }
    }
}
