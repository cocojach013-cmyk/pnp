using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ControlPNP.Data;
using SistemaControlPNP.Models;

namespace ControlPNP.Controllers
{
    [Authorize]
    public class MovimientoController : Controller
    {
        private readonly ControlDBContext _db;

        public MovimientoController(ControlDBContext db)
        {
            _db = db;
        }

        // =======================
        // ======= DTOs ==========
        // =======================

        public sealed class CrearMovimientoDto
        {
            public int IdPersonal { get; set; }
            // Opcionales; si no llegan, se usa la hora actual de Lima
            public string? Fecha { get; set; }     // yyyy-MM-dd (hora local Lima)
            public string? Hora { get; set; }      // HH:mm (hora local Lima)
            public string Motivo { get; set; } = "";
            public string Autoriza { get; set; } = "";
            public string? Destino { get; set; }
            public string? Novedades { get; set; }
        }

        public sealed class EditarMovimientoDto
        {
            public long IdMovimiento { get; set; }
            public int IdPersonal { get; set; }
            public string Fecha { get; set; } = "";        // yyyy-MM-dd
            public string Hora { get; set; } = "";         // HH:mm
            public string Motivo { get; set; } = "";
            public string Autoriza { get; set; } = "";
            public string? Destino { get; set; }
            public string? Novedades { get; set; }

            // NUEVO: retorno
            public string? FechaRetorno { get; set; }      // yyyy-MM-dd | null
            public string? HoraRetorno { get; set; }       // HH:mm | null
        }

        // DTO para retorno desde AJAX (JSON)
        public sealed class RegistrarRetornoDto
        {
            public long idMovimiento { get; set; }
            public string? novedades { get; set; }
        }

        // =======================
        // ======= LISTA =========
        // =======================

        [HttpGet]
        public async Task<IActionResult> ListaMovimiento()
        {
            try
            {
                var tz = GetLimaTz();

                var data = await _db.Movimiento
                    .Include(m => m.Personal)
                    .OrderByDescending(m => m.FechaSalida)
                    .ToListAsync();

                var lista = data.Select(m =>
                {
                    var salidaLocal = TimeZoneInfo.ConvertTimeFromUtc(m.FechaSalida, tz);
                    DateTime? retornoLocal = m.FechaRetorno.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(m.FechaRetorno.Value, tz)
                        : (DateTime?)null;

                    return new MovimientoViewModel
                    {
                        IdMovimiento = m.IdMovimiento,
                        IdPersonal = m.IdPersonal,
                        Grado = m.Personal?.Grado ?? "",
                        Apellidos = m.Personal?.Apellidos ?? "",
                        Nombres = m.Personal?.Nombres ?? "",
                        Unidad = m.Personal?.Unidad ?? "",
                        Motivo = m.Motivo ?? "",
                        Autoriza = m.Autoriza ?? "",
                        Destino = string.IsNullOrWhiteSpace(m.Destino) ? "" : m.Destino,
                        Novedades = string.IsNullOrWhiteSpace(m.Novedades) ? "" : m.Novedades,
                        FechaSalidaLocal = salidaLocal,
                        FechaRetornoLocal = retornoLocal
                    };
                });

                return View(lista);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al listar movimientos: {ex.Message}" });
            }
        }

        // =======================
        // ===== CREAR (GET) =====
        // =======================

        [HttpGet]
        public async Task<IActionResult> RegistrarMovimiento()
        {
            try
            {
                await CargarPersonalConAsistenciaAbiertaEnViewBag(); // activos + asistencia abierta (cualquier día) + sin movimiento abierto
                return PartialView(); // Views/Movimiento/RegistrarMovimiento.cshtml
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al preparar el formulario: {ex.Message}" });
            }
        }

        // =======================
        // ===== CREAR (POST) ====
        // =======================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarMovimiento([FromForm] CrearMovimientoDto dto)
        {
            try
            {
                if (dto == null || dto.IdPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                if (string.IsNullOrWhiteSpace(dto.Motivo) || string.IsNullOrWhiteSpace(dto.Autoriza))
                    return Json(new { success = false, message = "Motivo y Autoriza son obligatorios." });

                // Personal activo
                var persona = await _db.Personal.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdPersonal == dto.IdPersonal && p.Estado);
                if (persona == null)
                    return Json(new { success = false, message = "El personal no existe o está inactivo." });

                // Asistencia abierta HOY
                var (startUtc, endUtc, _, _) = TodayWindowUtcForLima();
                var tieneAbierta = await _db.Asistencia.AsNoTracking()
                                  .AnyAsync(a => a.IdPersonal == dto.IdPersonal
                                              && a.FechaIngreso >= startUtc && a.FechaIngreso < endUtc
                                              && a.FechaSalida == null);
                if (!tieneAbierta)
                    return Json(new { success = false, message = "Este personal no tiene ingreso registrado hoy o ya marcó salida." });

                // NO permitir movimiento si ya tiene uno sin retorno
                var hayMovimientoAbierto = await _db.Movimiento.AsNoTracking()
                    .AnyAsync(m => m.IdPersonal == dto.IdPersonal && m.FechaRetorno == null);
                if (hayMovimientoAbierto)
                    return Json(new { success = false, message = "El personal ya tiene un movimiento sin fecha de retorno." });

                // Fecha/Hora salida -> UTC
                var tz = GetLimaTz();
                DateTime salidaUtc;
                try
                {
                    DateTime local;
                    if (string.IsNullOrWhiteSpace(dto.Fecha) || string.IsNullOrWhiteSpace(dto.Hora))
                    {
                        var nowUtc = DateTime.UtcNow;
                        local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
                    }
                    else
                    {
                        local = DateTime.ParseExact($"{dto.Fecha} {dto.Hora}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);
                    }
                    local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                    salidaUtc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                }
                catch
                {
                    return Json(new { success = false, message = "Fecha/Hora inválidas." });
                }

                var mov = new Movimiento
                {
                    IdPersonal = dto.IdPersonal,
                    FechaSalida = salidaUtc,
                    Motivo = dto.Motivo.Trim(),
                    Autoriza = dto.Autoriza.Trim(),
                    Destino = string.IsNullOrWhiteSpace(dto.Destino) ? "" : dto.Destino.Trim(),
                    Novedades = string.IsNullOrWhiteSpace(dto.Novedades) ? "" : dto.Novedades.Trim()
                };

                try
                {
                    _db.Movimiento.Add(mov);
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, id = mov.IdMovimiento });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo registrar el movimiento.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar movimiento: {ex.Message}" });
            }
        }

        // =======================
        // ===== EDITAR (GET) ====
        // =======================

        [HttpGet]
        public async Task<IActionResult> EditarMovimiento(long id)
        {
            try
            {
                if (id <= 0) return BadRequest();

                var mov = await _db.Movimiento
                    .Include(m => m.Personal)
                    .FirstOrDefaultAsync(m => m.IdMovimiento == id);

                if (mov == null) return NotFound();

                // Lista para el combo (activos + asistencia abierta + sin movimiento abierto)
                // pero asegurar que el actualmente asignado aparezca siempre
                await CargarPersonalConAsistenciaAbiertaEnViewBag(incluirIdPersonalExtra: mov.IdPersonal);

                var tz = GetLimaTz();
                var salidaLocal = TimeZoneInfo.ConvertTimeFromUtc(mov.FechaSalida, tz);

                ViewBag.Fecha = salidaLocal.ToString("yyyy-MM-dd");
                ViewBag.Hora = salidaLocal.ToString("HH:mm");

                if (mov.FechaRetorno.HasValue)
                {
                    var retLocal = TimeZoneInfo.ConvertTimeFromUtc(mov.FechaRetorno.Value, tz);
                    ViewBag.FechaRetorno = retLocal.ToString("yyyy-MM-dd");
                    ViewBag.HoraRetorno = retLocal.ToString("HH:mm");
                }
                else
                {
                    ViewBag.FechaRetorno = "";
                    ViewBag.HoraRetorno = "";
                }

                return PartialView("EditarMovimiento", mov);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al cargar edición: {ex.Message}" });
            }
        }

        // =======================
        // ===== EDITAR (POST) ===
        // =======================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarMovimiento([FromForm] EditarMovimientoDto dto)
        {
            try
            {
                if (dto == null || dto.IdMovimiento <= 0)
                    return Json(new { success = false, message = "Solicitud inválida." });

                if (string.IsNullOrWhiteSpace(dto.Motivo) || string.IsNullOrWhiteSpace(dto.Autoriza))
                    return Json(new { success = false, message = "Motivo y Autoriza son obligatorios." });

                var mov = await _db.Movimiento.FirstOrDefaultAsync(m => m.IdMovimiento == dto.IdMovimiento);
                if (mov == null)
                    return Json(new { success = false, message = "Movimiento no encontrado." });

                // Persona activa
                var persona = await _db.Personal.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdPersonal == dto.IdPersonal && p.Estado);
                if (persona == null)
                    return Json(new { success = false, message = "El personal no existe o está inactivo." });

                var tz = GetLimaTz();

                // Parse salida
                DateTime salidaUtc;
                try
                {
                    var salidaLocal = DateTime.ParseExact($"{dto.Fecha} {dto.Hora}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    salidaLocal = DateTime.SpecifyKind(salidaLocal, DateTimeKind.Unspecified);
                    salidaUtc = TimeZoneInfo.ConvertTimeToUtc(salidaLocal, tz);
                }
                catch
                {
                    return Json(new { success = false, message = "Fecha/Hora de salida inválidas." });
                }

                // Parse retorno (opcional), validar >= salida
                DateTime? retornoUtc = null;
                if (!string.IsNullOrWhiteSpace(dto.FechaRetorno) || !string.IsNullOrWhiteSpace(dto.HoraRetorno))
                {
                    if (string.IsNullOrWhiteSpace(dto.FechaRetorno) || string.IsNullOrWhiteSpace(dto.HoraRetorno))
                        return Json(new { success = false, message = "Complete fecha y hora de retorno, o deje ambos vacíos." });

                    try
                    {
                        var retLocal = DateTime.ParseExact($"{dto.FechaRetorno} {dto.HoraRetorno}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        retLocal = DateTime.SpecifyKind(retLocal, DateTimeKind.Unspecified);
                        retornoUtc = TimeZoneInfo.ConvertTimeToUtc(retLocal, tz);
                    }
                    catch
                    {
                        return Json(new { success = false, message = "Fecha/Hora de retorno inválidas." });
                    }

                    if (retornoUtc.Value < salidaUtc)
                        return Json(new { success = false, message = "La fecha/hora de retorno no puede ser menor a la de salida." });
                }

                // Asignaciones
                mov.IdPersonal = dto.IdPersonal;
                mov.FechaSalida = salidaUtc;
                mov.FechaRetorno = retornoUtc;
                mov.Motivo = (dto.Motivo ?? "").Trim();
                mov.Autoriza = (dto.Autoriza ?? "").Trim();
                mov.Destino = string.IsNullOrWhiteSpace(dto.Destino) ? "" : dto.Destino.Trim();
                mov.Novedades = string.IsNullOrWhiteSpace(dto.Novedades) ? "" : dto.Novedades.Trim();

                // Verificar si realmente hubo cambios antes de guardar
                var entry = _db.Entry(mov);
                bool hayCambios = entry.Properties.Any(p => p.IsModified);
                if (!hayCambios)
                    return Json(new { success = false, message = "No se realizaron cambios." });

                try
                {
                    var afectados = await _db.SaveChangesAsync();
                    if (afectados == 0)
                        return Json(new { success = false, message = "No se realizaron cambios." });

                    return Json(new { success = true });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo actualizar el movimiento.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al actualizar movimiento: {ex.Message}" });
            }
        }

        // =======================
        // ===== REGISTRAR RETORNO
        // =======================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRetorno([FromBody] RegistrarRetornoDto dto)
        {
            try
            {
                if (dto == null || dto.idMovimiento <= 0)
                    return Json(new { success = false, message = "Solicitud inválida." });

                var mov = await _db.Movimiento.FirstOrDefaultAsync(m => m.IdMovimiento == dto.idMovimiento);
                if (mov == null)
                    return Json(new { success = false, message = "Movimiento no encontrado." });

                if (mov.FechaRetorno.HasValue)
                    return Json(new { success = false, message = "El movimiento ya tiene retorno registrado." });

                var tz = GetLimaTz();
                var nowUtc = DateTime.UtcNow;
                var nowLima = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

                mov.FechaRetorno = nowUtc;

                var nov = (dto.novedades ?? "").Trim();
                mov.Novedades = string.IsNullOrWhiteSpace(nov) ? "SIN NOVEDAD" : nov;

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Retorno registrado ({nowLima:dd/MM/yyyy HH:mm:ss})."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar retorno: {ex.Message}" });
            }
        }

        // =======================
        // ===== Helpers =========
        // =======================

        private async Task CargarPersonalConAsistenciaAbiertaEnViewBag(int? incluirIdPersonalExtra = null)
        {
            try
            {
                // IdPersonal con ASISTENCIA ABIERTA (FechaSalida == null)
                var idsConAsistenciaAbierta = await _db.Asistencia.AsNoTracking()
                    .Where(a => a.FechaSalida == null)
                    .Select(a => a.IdPersonal)
                    .Distinct()
                    .ToListAsync();

                // IdPersonal con MOVIMIENTO ABIERTO (FechaRetorno == null)
                var idsConMovimientoAbierto = await _db.Movimiento.AsNoTracking()
                    .Where(m => m.FechaRetorno == null)
                    .Select(m => m.IdPersonal)
                    .Distinct()
                    .ToListAsync();

                // PERSONALES:
                //  - Activos
                //  - Con asistencia abierta (cualquier día)
                //  - Sin movimiento abierto
                var personas = await _db.Personal.AsNoTracking()
                    .Where(p => p.Estado
                                && idsConAsistenciaAbierta.Contains(p.IdPersonal)
                                && !idsConMovimientoAbierto.Contains(p.IdPersonal))
                    .OrderBy(p => p.Grado)
                    .ThenBy(p => p.Apellidos)
                    .ThenBy(p => p.Nombres)
                    .ToListAsync();

                // Asegurar que el personal actualmente asignado aparezca (en edición)
                if (incluirIdPersonalExtra.HasValue && !personas.Any(p => p.IdPersonal == incluirIdPersonalExtra.Value))
                {
                    var actual = await _db.Personal.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.IdPersonal == incluirIdPersonalExtra.Value);
                    if (actual != null)
                        personas.Insert(0, actual);
                }

                ViewBag.PersonalList = personas.Select(p => new SelectListItem
                {
                    Value = p.IdPersonal.ToString(),
                    Text = $"{p.Grado} - {p.Apellidos} {p.Nombres} (CIP {p.NumeroCip})"
                }).ToList();
            }
            catch (Exception ex)
            {
                // En caso de error, deja el combo vacío y pasa el mensaje (opcional)
                ViewBag.PersonalList = new[]
                {
                    new SelectListItem { Value = "", Text = $"(Error al cargar personal: {ex.Message})" }
                }.ToList();
            }
        }

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
            var start = nowLima.Date;
            var end = start.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(start, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(end, tz);
            return (startUtc, endUtc, nowUtc, nowLima.ToString("dd/MM/yyyy HH:mm:ss"));
        }
    }
}
