using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ControlPNP.Data;
using SistemaControlPNP.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using ControlPNP.Filters;
using ControlPNP.ViewModels;

namespace ControlPNP.Controllers
{
    [Authorize]
    public class AsistenciaController : Controller
    {
        private readonly ControlDBContext _db;

        public AsistenciaController(ControlDBContext db)
        {
            _db = db;
        }

        // ===== DTOs =====
        public sealed class RegistrarAsistenciaRequest { public int idPersonal { get; set; } }
        public sealed class RegistrarSalidaRequest { public int idPersonal { get; set; } }
        public sealed class ActualizarAsistenciaDto
        {
            public long IdAsistencia { get; set; }
            public int IdPersonal { get; set; }
            public string? IngresoFecha { get; set; }
            public string? IngresoHora { get; set; }
            public string? SalidaFecha { get; set; }
            public string? SalidaHora { get; set; }
            public string? Control_Asistencia { get; set; }
        }
        public sealed class CerrarAsisDto { public long IdAsistencia { get; set; } }

        // ===== Helpers =====
        public static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        public static (DateTime startUtc, DateTime endUtc, DateTime nowUtc, string nowStrLima) TodayWindowUtcForLima()
        {
            var tz = GetLimaTz();
            var nowUtc = DateTime.UtcNow;
            var nowLima = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var startLima = nowLima.Date;
            var endLima = startLima.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLima, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLima, tz);
            return (startUtc, endUtc, nowUtc, nowLima.ToString("dd/MM/yyyy HH:mm:ss"));
        }

        /// <summary>
        /// Llena el combo de Personal (solo activos). Marca seleccionado si se provee id.
        /// </summary>
        private async Task<List<SelectListItem>> BuildPersonalItemsAsync(int? selectedId = null)
        {
            return await _db.Personal
                .AsNoTracking()
                .Where(p => p.Estado)
                .OrderBy(p => p.Grado)
                .ThenBy(p => p.Apellidos)
                .ThenBy(p => p.Nombres)
                .Select(p => new SelectListItem
                {
                    Value = p.IdPersonal.ToString(),
                    Text = $"{p.Grado} - {p.Apellidos} {p.Nombres} (CIP {p.NumeroCip})",
                    Selected = selectedId.HasValue && p.IdPersonal == selectedId.Value
                })
                .ToListAsync();
        }

        // ===== Registrar / Salida =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("ASISTENCIA_TIENE_ABIERTA", registroKey: "idPersonal")]
        public async Task<IActionResult> TieneAsistenciaAbierta([FromBody] RegistrarSalidaRequest req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { ok = false, abierta = false, message = "Personal no válido." });

                // Cualquier asistencia abierta (sin salida), sin importar la fecha
                var abierta = await _db.Asistencia.AsNoTracking()
                    .AnyAsync(a => a.IdPersonal == req.idPersonal
                                && a.FechaSalida == null);

                return Json(new { ok = true, abierta });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = $"Error al verificar asistencia: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Registrar()
        {
            try
            {
                // Personas que tienen cualquier asistencia abierta (de hoy o días anteriores)
                var idsConAbierta = await _db.Asistencia
                    .AsNoTracking()
                    .Where(a => a.FechaSalida == null)
                    .Select(a => a.IdPersonal)
                    .Distinct()
                    .ToListAsync();

                var items = await _db.Personal
                    .AsNoTracking()
                    .Where(p => p.Estado && !idsConAbierta.Contains(p.IdPersonal))
                    .OrderBy(p => p.Grado).ThenBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Select(p => new SelectListItem
                    {
                        Value = p.IdPersonal.ToString(),
                        Text = $"{p.Grado} - {p.Apellidos} {p.Nombres} (CIP {p.NumeroCip})"
                    })
                    .ToListAsync();

                ViewBag.PersonalList = items;
                return PartialView("RegistrarAsistencia");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al cargar formulario: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("ASISTENCIA_REGISTRAR_INGRESO", registroKey: "idPersonal")]
        public async Task<IActionResult> RegistrarAsistencia([FromBody] RegistrarAsistenciaRequest req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                var persona = await _db.Personal.FindAsync(req.idPersonal);
                if (persona == null || !persona.Estado)
                    return Json(new { success = false, message = "Personal no válido." });

                var (startUtc, endUtc, nowUtc, nowStrLima) = TodayWindowUtcForLima();

                // Cualquier asistencia abierta (sin salida), sin importar la fecha
                var hayAbierta = await _db.Asistencia.AsNoTracking()
                    .AnyAsync(a => a.IdPersonal == req.idPersonal
                                && a.FechaSalida == null);

                if (hayAbierta)
                    return Json(new { success = false, message = "Ya existe una asistencia abierta." });

                _db.Asistencia.Add(new Asistencia
                {
                    IdPersonal = req.idPersonal,
                    FechaIngreso = nowUtc,
                    Control_Asistencia = "Web"
                });

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"Asistencia registrada ({nowStrLima})." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar asistencia: {ex.Message}" });
            }
        }

        // ===== MÉTODO MODIFICADO =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("ASISTENCIA_REGISTRAR_SALIDA", registroKey: "idPersonal")]
        public async Task<IActionResult> RegistrarSalida([FromBody] RegistrarSalidaRequest req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                // Hora actual (UTC y Lima) para registrar y mostrar
                var tz = GetLimaTz();
                var nowUtc = DateTime.UtcNow;
                var nowLima = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
                var nowStrLima = nowLima.ToString("dd/MM/yyyy HH:mm:ss");

                // 1) Intentar encontrar asistencia abierta de HOY
                var (startUtc, endUtc, _, _) = TodayWindowUtcForLima();

                var abierta = await _db.Asistencia
                    .Where(a => a.IdPersonal == req.idPersonal
                             && a.FechaIngreso >= startUtc && a.FechaIngreso < endUtc
                             && a.FechaSalida == null)
                    .OrderByDescending(a => a.FechaIngreso)
                    .FirstOrDefaultAsync();

                // 2) Si no hay de hoy, buscar cualquier asistencia abierta (de días anteriores)
                if (abierta == null)
                {
                    abierta = await _db.Asistencia
                        .Where(a => a.IdPersonal == req.idPersonal && a.FechaSalida == null)
                        .OrderByDescending(a => a.FechaIngreso)
                        .FirstOrDefaultAsync();
                }

                if (abierta == null)
                    return Json(new { success = false, message = "No hay asistencia abierta." });

                // Cerramos la asistencia encontrada
                abierta.FechaSalida = nowUtc;
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Salida registrada ({nowStrLima})." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar salida: {ex.Message}" });
            }
        }

        // ===== Lista =====
        public async Task<IActionResult> ListaAsistencia()
        {
            try
            {
                var tz = GetLimaTz();

                var asistencias = await _db.Asistencia
                    .Include(a => a.Personal)
                    .OrderBy(a => a.Personal.Grado)
                    .ThenBy(a => a.Personal.Apellidos)
                    .ThenBy(a => a.Personal.Nombres)
                    .ToListAsync();

                var listaVM = asistencias.Select(a => new AsistenciaViewModel
                {
                    IdAsistencia = a.IdAsistencia,
                    IdPersonal = a.IdPersonal,
                    Grado = a.Personal?.Grado ?? "",
                    Nombres = a.Personal?.Nombres ?? "",
                    Apellidos = a.Personal?.Apellidos ?? "",
                    Unidad = a.Personal?.Unidad ?? "",
                    FechaIngreso = TimeZoneInfo.ConvertTimeFromUtc(a.FechaIngreso, tz),
                    FechaSalida = a.FechaSalida.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(a.FechaSalida.Value, tz) : (DateTime?)null,
                    Control_Asistencia = a.Control_Asistencia ?? ""
                }).AsEnumerable();

                return View(listaVM);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al listar asistencias: {ex.Message}" });
            }
        }

        // ===== Actualización =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("ASISTENCIA_ACTUALIZAR", registroKey: "IdAsistencia")]
        public async Task<IActionResult> ActualizarAsistencia([FromBody] ActualizarAsistenciaDto dto)
        {
            try
            {
                if (dto == null || dto.IdAsistencia <= 0)
                    return Json(new { success = false, message = "Solicitud inválida." });

                var per = await _db.Personal.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.IdPersonal == dto.IdPersonal && p.Estado);
                if (per == null)
                    return Json(new { success = false, message = "Personal no válido." });

                var asis = await _db.Asistencia.FirstOrDefaultAsync(a => a.IdAsistencia == dto.IdAsistencia);
                if (asis == null)
                    return Json(new { success = false, message = "Asistencia no encontrada." });

                var tz = GetLimaTz();
                if (string.IsNullOrWhiteSpace(dto.IngresoFecha) || string.IsNullOrWhiteSpace(dto.IngresoHora))
                    return Json(new { success = false, message = "Completa fecha y hora de ingreso." });

                DateTime ingresoLocal, ingresoUtc;
                ingresoLocal = DateTime.ParseExact($"{dto.IngresoFecha} {dto.IngresoHora}",
                                                   "yyyy-MM-dd HH:mm",
                                                   CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None);
                ingresoLocal = DateTime.SpecifyKind(ingresoLocal, DateTimeKind.Unspecified);
                ingresoUtc = TimeZoneInfo.ConvertTimeToUtc(ingresoLocal, tz);

                DateTime? salidaUtc = null;
                if (!string.IsNullOrWhiteSpace(dto.SalidaFecha) && !string.IsNullOrWhiteSpace(dto.SalidaHora))
                {
                    var salidaLocal = DateTime.ParseExact($"{dto.SalidaFecha} {dto.SalidaHora}",
                                                          "yyyy-MM-dd HH:mm",
                                                          CultureInfo.InvariantCulture,
                                                          DateTimeStyles.None);
                    salidaLocal = DateTime.SpecifyKind(salidaLocal, DateTimeKind.Unspecified);
                    salidaUtc = TimeZoneInfo.ConvertTimeToUtc(salidaLocal, tz);

                    if (salidaUtc < ingresoUtc)
                        return Json(new { success = false, message = "La salida no puede ser anterior al ingreso." });
                }

                asis.IdPersonal = dto.IdPersonal;
                asis.FechaIngreso = ingresoUtc;
                asis.FechaSalida = salidaUtc;
                if (dto.Control_Asistencia != null)
                    asis.Control_Asistencia = dto.Control_Asistencia.Trim();

                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "Error al guardar cambios. Revisa los datos e intenta nuevamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al actualizar: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("ASISTENCIA_CERRAR_AHORA", registroKey: "IdAsistencia")]
        public async Task<IActionResult> CerrarAsistenciaAhora([FromBody] CerrarAsisDto dto)
        {
            try
            {
                if (dto == null || dto.IdAsistencia <= 0)
                    return Json(new { success = false, message = "Solicitud inválida." });

                var asis = await _db.Asistencia.FirstOrDefaultAsync(a => a.IdAsistencia == dto.IdAsistencia);
                if (asis == null) return Json(new { success = false, message = "Asistencia no encontrada." });
                if (asis.FechaSalida.HasValue) return Json(new { success = false, message = "Esta asistencia ya tiene salida." });

                asis.FechaSalida = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al cerrar asistencia: {ex.Message}" });
            }
        }

        // ===== Editar (GET) =====
        [HttpGet]
        public async Task<IActionResult> EditarAsistencia(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest("Id inválido.");

                var asis = await _db.Asistencia
                    .AsNoTracking()
                    .Include(a => a.Personal)
                    .FirstOrDefaultAsync(a => a.IdAsistencia == id);

                if (asis == null)
                    return NotFound("Asistencia no encontrada.");

                // Combo de Personal para la vista parcial
                ViewBag.PersonalList = await BuildPersonalItemsAsync(asis.IdPersonal);

                // Debe existir Views/Asistencia/EditarAsistencia.cshtml (Layout = null)
                return PartialView("EditarAsistencia", asis);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al cargar formulario de edición: {ex.Message}");
            }
        }
    }
}
