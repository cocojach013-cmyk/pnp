using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ControlPNP.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ControlPNP.Controllers
{
    [Authorize]
    [Route("Reportes/Personal")]
    public class ReportePersonalController : Controller
    {
        private readonly ControlDBContext _db;
        public ReportePersonalController(ControlDBContext db) => _db = db;

        private static int Edad(DateTime fechaNacLocal, DateTime hoyLocal)
        {
            var e = hoyLocal.Year - fechaNacLocal.Year;
            if (hoyLocal.Date < fechaNacLocal.Date.AddYears(e)) e--;
            return e;
        }

        private static DateTime? ParseDate(string? ymd)
        {
            if (string.IsNullOrWhiteSpace(ymd)) return null;
            return DateTime.TryParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out var d) ? d.Date : (DateTime?)null;
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            return View("~/Views/Reportes/Personal.cshtml");
        }

        // JSON con filtros avanzados
        [HttpGet("Json")]
        public async Task<IActionResult> Json(
            string? sexo = null,
            int? edadMin = null, int? edadMax = null,
            string? estado = null,
            [FromQuery] string[]? unidad = null,
            [FromQuery] string[]? grado = null,
            // Rangos de fechas:
            string? fnacDesde = null, string? fnacHasta = null,          // FechaNacimiento (date)
            string? fincDesde = null, string? fincHasta = null           // FechaRegistro (datetime)
        )
        {
            var hoyLocal = DateTime.UtcNow.Date; // para cálculo de edad

            // Parse rangos
            var fNacD = ParseDate(fnacDesde);
            var fNacH = ParseDate(fnacHasta);
            var fIncD = ParseDate(fincDesde);
            var fIncH = ParseDate(fincHasta)?.AddDays(1); // exclusivo

            var q = _db.Personal.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var sx = sexo.Trim().ToUpperInvariant();
                q = q.Where(p => p.Sexo == sx);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                if (bool.TryParse(estado, out var st))
                    q = q.Where(p => p.Estado == st);
            }

            if (unidad != null && unidad.Length > 0)
            {
                var set = unidad.Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => s.Trim())
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Count > 0)
                    q = q.Where(p => p.Unidad != null && set.Contains(p.Unidad));
            }

            if (grado != null && grado.Length > 0)
            {
                var set = grado.Where(s => !string.IsNullOrWhiteSpace(s))
                               .Select(s => s.Trim())
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Count > 0)
                    q = q.Where(p => p.Grado != null && set.Contains(p.Grado));
            }

            // Rango Fecha Nacimiento (date)
            if (fNacD.HasValue) q = q.Where(p => p.FechaNacimiento >= fNacD.Value);
            if (fNacH.HasValue) q = q.Where(p => p.FechaNacimiento <= fNacH.Value);

            // Rango Fecha Registro (datetime) — se usa como fecha (inicio incluido, fin exclusivo)
            if (fIncD.HasValue) q = q.Where(p => p.FechaRegistro >= fIncD.Value);
            if (fIncH.HasValue) q = q.Where(p => p.FechaRegistro < fIncH.Value);

            var data = await q.OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                              .Take(100000).ToListAsync();

            var rows = data.Select(p =>
            {
                int? e = null;
                if (p.FechaNacimiento != default) e = Edad(p.FechaNacimiento, hoyLocal);

                return new
                {
                    cip = p.NumeroCip,
                    apellidos = p.Apellidos,
                    nombres = p.Nombres,
                    sexo = p.Sexo,
                    edad = e,
                    grado = p.Grado,
                    unidad = p.Unidad,
                    dni = p.Dni,
                    correo = p.CorreoInstitucional,
                    estado = p.Estado ? "Activo" : "Inactivo",
                    fechaNacimiento = p.FechaNacimiento.ToString("dd/MM/yyyy"),
                    fechaRegistro = (p.FechaRegistro == default)
                        ? ""
                        : p.FechaRegistro.ToString("dd/MM/yyyy HH:mm")
                };
            }).ToList();

            // Filtro final por edad si llegó después
            if (edadMin.HasValue) rows = rows.Where(r => r.edad.HasValue && r.edad.Value >= edadMin.Value).ToList();
            if (edadMax.HasValue) rows = rows.Where(r => r.edad.HasValue && r.edad.Value <= edadMax.Value).ToList();

            return Json(rows);
        }

        [HttpGet("ExportCsv")]
        public async Task<IActionResult> ExportCsv(
            string? sexo = null,
            int? edadMin = null, int? edadMax = null,
            string? estado = null,
            [FromQuery] string[]? unidad = null,
            [FromQuery] string[]? grado = null,
            string? fnacDesde = null, string? fnacHasta = null,
            string? fincDesde = null, string? fincHasta = null
        )
        {
            var hoyLocal = DateTime.UtcNow.Date;
            var fNacD = ParseDate(fnacDesde);
            var fNacH = ParseDate(fnacHasta);
            var fIncD = ParseDate(fincDesde);
            var fIncH = ParseDate(fincHasta)?.AddDays(1);

            var q = _db.Personal.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var sx = sexo.Trim().ToUpperInvariant();
                q = q.Where(p => p.Sexo == sx);
            }
            if (!string.IsNullOrWhiteSpace(estado))
            {
                if (bool.TryParse(estado, out var st))
                    q = q.Where(p => p.Estado == st);
            }
            if (unidad != null && unidad.Length > 0)
            {
                var set = unidad.Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => s.Trim())
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Count > 0)
                    q = q.Where(p => p.Unidad != null && set.Contains(p.Unidad));
            }
            if (grado != null && grado.Length > 0)
            {
                var set = grado.Where(s => !string.IsNullOrWhiteSpace(s))
                               .Select(s => s.Trim())
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Count > 0)
                    q = q.Where(p => p.Grado != null && set.Contains(p.Grado));
            }
            if (fNacD.HasValue) q = q.Where(p => p.FechaNacimiento >= fNacD.Value);
            if (fNacH.HasValue) q = q.Where(p => p.FechaNacimiento <= fNacH.Value);
            if (fIncD.HasValue) q = q.Where(p => p.FechaRegistro >= fIncD.Value);
            if (fIncH.HasValue) q = q.Where(p => p.FechaRegistro < fIncH.Value);

            var data = await q.OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                              .Take(100000).ToListAsync();

            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("CIP;Apellidos;Nombres;Sexo;Edad;Grado;Unidad;DNI;Correo;Estado;FechaNacimiento;FechaRegistro");

            foreach (var p in data)
            {
                int? e = null;
                if (p.FechaNacimiento != default) e = Edad(p.FechaNacimiento, hoyLocal);

                if (edadMin.HasValue && (!e.HasValue || e.Value < edadMin.Value)) continue;
                if (edadMax.HasValue && (!e.HasValue || e.Value > edadMax.Value)) continue;

                string Esc(string? s) => (s ?? "").Replace(";", ",").Replace(Environment.NewLine, " ").Trim();

                var fn = p.FechaNacimiento.ToString("dd/MM/yyyy");
                var fr = (p.FechaRegistro == default) ? "" : p.FechaRegistro.ToString("dd/MM/yyyy HH:mm");

                sb.AppendLine($"{Esc(p.NumeroCip)};{Esc(p.Apellidos)};{Esc(p.Nombres)};{Esc(p.Sexo)};{e};{Esc(p.Grado)};{Esc(p.Unidad)};{Esc(p.Dni)};{Esc(p.CorreoInstitucional)};{(p.Estado ? "Activo" : "Inactivo")};{fn};{fr}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", $"reporte_personal_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }
    }
}
