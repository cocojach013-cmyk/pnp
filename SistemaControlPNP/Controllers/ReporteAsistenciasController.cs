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
    [Route("Reportes/Asistencia")]
    public class ReporteAsistenciaController : Controller
    {
        private readonly ControlDBContext _db;
        public ReporteAsistenciaController(ControlDBContext db) => _db = db;

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

        // A tiempo / Tarde / Temprano (temprano = antes del corte exacto; a tiempo = exactamente al corte)
        private static (string estado, int minLate, int minEarly) EvalLlegada(DateTime ingresoUtc)
        {
            var tz = GetLimaTz();
            var local = TimeZoneInfo.ConvertTimeFromUtc(ingresoUtc, tz);
            var cutoff = new DateTime(local.Year, local.Month, local.Day, 7, 45, 0);

            if (local < cutoff)
            {
                var early = (int)Math.Ceiling((cutoff - local).TotalMinutes);
                return ("Temprano", 0, Math.Max(early, 0));
            }
            if (local > cutoff)
            {
                var late = (int)Math.Ceiling((local - cutoff).TotalMinutes);
                return ("Tarde", Math.Max(late, 0), 0);
            }
            return ("A tiempo", 0, 0);
        }

        private static (string Fecha, string Hora) Fmt(DateTime utc)
        {
            var tz = GetLimaTz();
            var l = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            return (l.ToString("dd/MM/yyyy"), l.ToString("HH:mm"));
        }

        // ===== VISTA =====
        [HttpGet("")]
        public IActionResult Index()
        {
            return View("~/Views/Reportes/Asistencia.cshtml");
        }

        // ===== JSON (para la vista) =====
        [HttpGet("Json")]
        public async Task<IActionResult> Json(
            string? desde = null, string? hasta = null,
            string? sexo = null, int? edadMin = null, int? edadMax = null,
            bool soloHoy = false)
        {
            var tz = GetLimaTz();
            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            DateTime? d1Utc = null, d2Utc = null;
            if (soloHoy)
            {
                d1Utc = TimeZoneInfo.ConvertTimeToUtc(todayLocal, tz);
                d2Utc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1), tz);
            }
            else
            {
                (d1Utc, d2Utc) = ParseDateRangeLocalToUtc(desde, hasta);
            }

            var q = _db.Asistencia
                .AsNoTracking()
                .Include(a => a.Personal)
                .AsQueryable();

            if (d1Utc.HasValue) q = q.Where(a => a.FechaIngreso >= d1Utc.Value);
            if (d2Utc.HasValue) q = q.Where(a => a.FechaIngreso < d2Utc.Value);

            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var sx = sexo.Trim().ToUpperInvariant();
                q = q.Where(a => a.Personal != null && a.Personal.Sexo == sx);
            }

            var data = await q.OrderByDescending(a => a.FechaIngreso)
                              .Take(50000)
                              .ToListAsync();

            var rows = data.Select(a =>
            {
                var p = a.Personal!;
                var (fIng, hIng) = Fmt(a.FechaIngreso);

                string fSal = "", hSal = "";
                if (a.FechaSalida.HasValue)
                {
                    (fSal, hSal) = Fmt(a.FechaSalida.Value);
                }

                int? edad = null;
                if (p.FechaNacimiento != default)
                {
                    edad = Edad(p.FechaNacimiento, todayLocal);
                }

                if (edadMin.HasValue && (!edad.HasValue || edad.Value < edadMin.Value)) return null;
                if (edadMax.HasValue && (!edad.HasValue || edad.Value > edadMax.Value)) return null;

                var eval = EvalLlegada(a.FechaIngreso);

                return new
                {
                    // Fechas/Horas (formateadas)
                    fecha = fIng,
                    horaIngreso = hIng,
                    fechaSalida = fSal,     // <-- FECHA SALIDA (agregada)
                    horaSalida = hSal,

                    // Estado
                    estadoLlegada = eval.estado,     // "A tiempo" | "Tarde" | "Temprano"
                    retrasoMin = eval.estado == "Tarde" ? eval.minLate : 0,

                    // Persona
                    cip = p.NumeroCip,
                    grado = p.Grado ?? "",           // <-- GRADO (agregado)
                    apellidos = p.Apellidos,
                    nombres = p.Nombres,
                    sexo = p.Sexo,
                    edad,

                    // Otros
                    unidad = p.Unidad,
                    control = a.Control_Asistencia ?? ""
                };
            })
            .Where(x => x != null)
            .ToList();

            return Json(rows);
        }

        // ===== CSV (Exportar) =====
        [HttpGet("ExportCsv")]
        public async Task<IActionResult> ExportCsv(
            string? desde = null, string? hasta = null,
            string? sexo = null, int? edadMin = null, int? edadMax = null,
            bool soloHoy = false)
        {
            var tz = GetLimaTz();
            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            DateTime? d1Utc = null, d2Utc = null;
            if (soloHoy)
            {
                d1Utc = TimeZoneInfo.ConvertTimeToUtc(todayLocal, tz);
                d2Utc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1), tz);
            }
            else
            {
                (d1Utc, d2Utc) = ParseDateRangeLocalToUtc(desde, hasta);
            }

            var q = _db.Asistencia.AsNoTracking().Include(a => a.Personal).AsQueryable();
            if (d1Utc.HasValue) q = q.Where(a => a.FechaIngreso >= d1Utc.Value);
            if (d2Utc.HasValue) q = q.Where(a => a.FechaIngreso < d2Utc.Value);
            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var sx = sexo.Trim().ToUpperInvariant();
                q = q.Where(a => a.Personal != null && a.Personal.Sexo == sx);
            }

            var data = await q.OrderByDescending(a => a.FechaIngreso).Take(100000).ToListAsync();

            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("Fecha;HoraIngreso;FechaSalida;HoraSalida;EstadoLlegada;RetrasoMin;CIP;Grado;Apellidos;Nombres;Sexo;Edad;Unidad;Control");

            foreach (var a in data)
            {
                var p = a.Personal!;
                var (fIng, hIng) = Fmt(a.FechaIngreso);
                var (fSal, hSal) = ("", "");
                if (a.FechaSalida.HasValue) (fSal, hSal) = Fmt(a.FechaSalida.Value);

                int? edad = null;
                if (p.FechaNacimiento != default) edad = Edad(p.FechaNacimiento, todayLocal);

                var eval = EvalLlegada(a.FechaIngreso);
                var estado = eval.estado;
                var ret = estado == "Tarde" ? eval.minLate : 0;

                string Esc(string? s) => (s ?? "").Replace(";", ",").Replace(Environment.NewLine, " ").Trim();

                sb.AppendLine($"{fIng};{hIng};{fSal};{hSal};{estado};{ret};{Esc(p.NumeroCip)};{Esc(p.Grado)};{Esc(p.Apellidos)};{Esc(p.Nombres)};{Esc(p.Sexo)};{edad};{Esc(p.Unidad)};{Esc(a.Control_Asistencia)}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", $"reporte_asistencia_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }
    }
}
