using ControlPNP.Data;
using ControlPNP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlPNP.Controllers
{
    [Authorize]
    public class AuditoriaController : Controller
    {
        private readonly ControlDBContext _db;

        public AuditoriaController(ControlDBContext db) => _db = db;

        private static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static (DateTime startUtc, DateTime endUtc)? LocalDateRangeToUtc(string? desde, string? hasta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(desde) && string.IsNullOrWhiteSpace(hasta)) return null;
                var tz = GetLimaTz(); DateTime? d1 = null, d2 = null;

                if (!string.IsNullOrWhiteSpace(desde) &&
                    DateTime.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x1))
                {
                    x1 = DateTime.SpecifyKind(x1, DateTimeKind.Unspecified);
                    d1 = TimeZoneInfo.ConvertTimeToUtc(x1, tz);
                }

                if (!string.IsNullOrWhiteSpace(hasta) &&
                    DateTime.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x2))
                {
                    x2 = DateTime.SpecifyKind(x2.AddDays(1), DateTimeKind.Unspecified);
                    d2 = TimeZoneInfo.ConvertTimeToUtc(x2, tz);
                }

                if (d1.HasValue && !d2.HasValue) d2 = d1.Value.AddDays(1);
                if (!d1.HasValue && d2.HasValue) d1 = d2.Value.AddDays(-1);
                if (!d1.HasValue || !d2.HasValue) return null;

                return (d1.Value, d2.Value);
            }
            catch
            {
                return null;
            }
        }

        // =====================================================
        // LISTA AUDITORÍA (vista principal)
        // =====================================================
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> ListaAuditoria(
    string? desde = null,
    string? hasta = null,
    int? idUsuario = null,
    string? usuario = null,
    string? accion = null,
    string? q = null,
    int page = 1,
    int pageSize = 50,
    string sort = "FechaHora_DESC")
        {
            try
            {
                page = Math.Max(1, page);

                // 👉 Aquí está la lógica correcta:
                // - pageSize == 0  => SIN paginación (mostrar todos)
                // - pageSize > 0   => se clampa entre 10 y 500
                if (pageSize < 0)
                {
                    pageSize = 50; // valor por defecto
                }

                bool sinLimite = (pageSize == 0);

                if (!sinLimite)
                {
                    pageSize = Math.Clamp(pageSize, 10, 500);
                }

                var query = _db.Auditoria
                    .AsNoTracking()
                    .Include(a => a.UsuarioRef)
                    .AsQueryable();

                var rangeUtc = LocalDateRangeToUtc(desde, hasta);
                if (rangeUtc.HasValue)
                {
                    var (s, e) = rangeUtc.Value;
                    query = query.Where(a => a.FechaHora >= s && a.FechaHora < e);
                }

                if (idUsuario.HasValue && idUsuario.Value > 0)
                    query = query.Where(a => a.IdUsuario == idUsuario.Value);

                if (!string.IsNullOrWhiteSpace(usuario))
                    query = query.Where(a => a.UsuarioRef != null && a.UsuarioRef.UsuarioLogin.Contains(usuario.Trim()));

                if (!string.IsNullOrWhiteSpace(accion))
                    query = query.Where(a => a.Accion.Contains(accion.Trim()));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    query = query.Where(a =>
                        (a.RegistroID ?? "").Contains(s) ||
                        (a.Descripcion ?? "").Contains(s) ||
                        (a.IpUsuario ?? "").Contains(s));
                }

                query = sort switch
                {
                    "Usuario_ASC" => query.OrderBy(a => a.UsuarioRef!.UsuarioLogin).ThenByDescending(a => a.FechaHora),
                    "Usuario_DESC" => query.OrderByDescending(a => a.UsuarioRef!.UsuarioLogin).ThenByDescending(a => a.FechaHora),
                    "Accion_ASC" => query.OrderBy(a => a.Accion).ThenByDescending(a => a.FechaHora),
                    "Accion_DESC" => query.OrderByDescending(a => a.Accion).ThenByDescending(a => a.FechaHora),
                    "FechaHora_ASC" => query.OrderBy(a => a.FechaHora),
                    _ => query.OrderByDescending(a => a.FechaHora),
                };

                var total = await query.CountAsync();

                IQueryable<Auditoria> pageQuery = query;

                // 👉 Aquí se decide si paginar o no
                if (!sinLimite)
                {
                    pageQuery = pageQuery
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize);
                }

                var items = await pageQuery.ToListAsync();

                var tz = GetLimaTz();
                var lista = items.Select(a => new AuditoriaViewModel
                {
                    IdAuditoria = a.IdAuditoria,
                    IdUsuario = a.IdUsuario,
                    UsuarioLogin = a.UsuarioRef?.UsuarioLogin ?? "(desconocido)",
                    Accion = a.Accion,
                    RegistroID = a.RegistroID,
                    Descripcion = a.Descripcion,
                    IpUsuario = a.IpUsuario,
                    FechaHoraLocal = TimeZoneInfo.ConvertTimeFromUtc(a.FechaHora, tz),
                    InicioSesionLocal = a.InicioSesion.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(a.InicioSesion.Value, tz)
                        : (DateTime?)null
                });

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.Total = total;
                ViewBag.Sort = sort;
                ViewBag.Desde = desde;
                ViewBag.Hasta = hasta;
                ViewBag.IdUsuario = idUsuario;
                ViewBag.Usuario = usuario;
                ViewBag.Accion = accion;
                ViewBag.Q = q;

                return View(lista);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al listar auditorías: {ex.Message}" });
            }
        }


        // =====================================================
        // LISTA JSON
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> ListaAuditoriaJson(
            string? desde = null, string? hasta = null, int? idUsuario = null,
            string? usuario = null, string? accion = null, string? q = null)
        {
            try
            {
                var tz = GetLimaTz();
                var query = _db.Auditoria.AsNoTracking().Include(a => a.UsuarioRef).AsQueryable();

                var rangeUtc = LocalDateRangeToUtc(desde, hasta);
                if (rangeUtc.HasValue)
                {
                    var (s, e) = rangeUtc.Value;
                    query = query.Where(a => a.FechaHora >= s && a.FechaHora < e);
                }

                if (idUsuario.HasValue && idUsuario.Value > 0)
                    query = query.Where(a => a.IdUsuario == idUsuario.Value);

                if (!string.IsNullOrWhiteSpace(usuario))
                    query = query.Where(a => a.UsuarioRef!.UsuarioLogin.Contains(usuario.Trim()));

                if (!string.IsNullOrWhiteSpace(accion))
                    query = query.Where(a => a.Accion.Contains(accion.Trim()));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    query = query.Where(a =>
                        (a.RegistroID ?? "").Contains(s) ||
                        (a.Descripcion ?? "").Contains(s) ||
                        (a.IpUsuario ?? "").Contains(s));
                }

                var data = await query.OrderByDescending(a => a.FechaHora).Take(5000).ToListAsync();

                var rows = data.Select(a => new
                {
                    a.IdAuditoria,
                    a.IdUsuario,
                    UsuarioLogin = a.UsuarioRef?.UsuarioLogin,
                    a.Accion,
                    a.RegistroID,
                    a.Descripcion,
                    a.IpUsuario,
                    FechaHoraLocal = TimeZoneInfo.ConvertTimeFromUtc(a.FechaHora, tz).ToString("yyyy-MM-dd HH:mm:ss"),
                    InicioSesionLocal = a.InicioSesion.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(a.InicioSesion.Value, tz).ToString("yyyy-MM-dd HH:mm:ss")
                        : null
                });

                return Json(rows);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener datos: {ex.Message}" });
            }
        }

        // =====================================================
        // EXPORTAR CSV
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> ExportarCsv(
            string? desde = null, string? hasta = null, int? idUsuario = null,
            string? usuario = null, string? accion = null, string? q = null, int top = 50000)
        {
            try
            {
                top = Math.Clamp(top, 1000, 200000);

                var tz = GetLimaTz();
                var query = _db.Auditoria.AsNoTracking().Include(a => a.UsuarioRef).AsQueryable();

                var rangeUtc = LocalDateRangeToUtc(desde, hasta);
                if (rangeUtc.HasValue)
                {
                    var (s, e) = rangeUtc.Value;
                    query = query.Where(a => a.FechaHora >= s && a.FechaHora < e);
                }

                if (idUsuario.HasValue && idUsuario.Value > 0)
                    query = query.Where(a => a.IdUsuario == idUsuario.Value);

                if (!string.IsNullOrWhiteSpace(usuario))
                    query = query.Where(a => a.UsuarioRef!.UsuarioLogin.Contains(usuario.Trim()));

                if (!string.IsNullOrWhiteSpace(accion))
                    query = query.Where(a => a.Accion.Contains(accion.Trim()));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    query = query.Where(a =>
                        (a.RegistroID ?? "").Contains(s) ||
                        (a.Descripcion ?? "").Contains(s) ||
                        (a.IpUsuario ?? "").Contains(s));
                }

                var data = await query.OrderByDescending(a => a.FechaHora).Take(top).ToListAsync();

                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("IdAuditoria;Fecha;Hora;Usuario;Accion;RegistroID;Descripcion;IP;InicioSesion");

                foreach (var a in data)
                {
                    var f = TimeZoneInfo.ConvertTimeFromUtc(a.FechaHora, tz);
                    var inicio = a.InicioSesion.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(a.InicioSesion.Value, tz).ToString("dd/MM/yyyy HH:mm")
                        : "";

                    string CsvEsc(string? s0) => (s0 ?? "").Replace(";", ",").Replace(Environment.NewLine, " ").Trim();

                    sb.AppendLine($"{a.IdAuditoria};{f:dd/MM/yyyy};{f:HH:mm};{CsvEsc(a.UsuarioRef?.UsuarioLogin)};" +
                                  $"{CsvEsc(a.Accion)};{CsvEsc(a.RegistroID)};{CsvEsc(a.Descripcion)};" +
                                  $"{CsvEsc(a.IpUsuario)};{CsvEsc(inicio)}");
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv; charset=utf-8", $"auditoria_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al exportar CSV: {ex.Message}" });
            }
        }
    }
}
