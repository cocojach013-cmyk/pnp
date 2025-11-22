using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using ControlPNP.Data;
using SistemaControlPNP.Models;

namespace ControlPNP.Controllers
{
    [AllowAnonymous]
    public class ControlController : Controller
    {
        private readonly ControlDBContext _db;
        public ControlController(ControlDBContext db) { _db = db; }

        // ===============================
        // ===== Zona horaria (Lima) =====
        // ===============================
        private static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static (DateTime startUtc, DateTime endUtc, DateTime nowUtc, string nowStrLima, DateTime nowLocal)
            TodayWindowUtcForLimaWithLocal()
        {
            var tz = GetLimaTz();
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var startLocal = nowLocal.Date;
            var endLocal = startLocal.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);
            return (startUtc, endUtc, nowUtc, nowLocal.ToString("dd/MM/yyyy HH:mm:ss"), nowLocal);
        }

        private static (DateTime startUtc, DateTime endUtc, DateTime nowUtc, string nowStrLima)
            TodayWindowUtcForLima()
        {
            var (s, e, n, str, _) = TodayWindowUtcForLimaWithLocal();
            return (s, e, n, str);
        }

        private static string ToLimaString(DateTime utc)
        {
            var tz = GetLimaTz();
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
            return local.ToString("dd/MM/yyyy HH:mm:ss");
        }

        // =========================
        // ===== Util: SHA-256 =====
        // =========================
        private static byte[] Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(data);
        }

        // ==================================================
        // ===== Llegada (A tiempo / Tarde) con cutoff 7:45
        // ==================================================
        private static (bool onTime, int minutesLate, int minutesEarly, string estadoLlegadaStr)
            EvalLlegada(DateTime fechaIngresoUtc)
        {
            var tz = GetLimaTz();
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fechaIngresoUtc, DateTimeKind.Utc), tz);
            var cutoff = new DateTime(local.Year, local.Month, local.Day, 7, 45, 0);
            if (local <= cutoff)
            {
                var diff = (int)Math.Ceiling((cutoff - local).TotalMinutes);
                return (true, 0, Math.Max(diff, 0), "A tiempo");
            }
            else
            {
                var diff = (int)Math.Ceiling((local - cutoff).TotalMinutes);
                return (false, Math.Max(diff, 0), 0, $"Tarde (+{Math.Max(diff, 0)} min)");
            }
        }

        // ==================
        // ====== DTOs ======
        // ==================
        public sealed class BuscarPersonaRequest
        {
            public string? criterio { get; set; }     // "cip" | "huella"
            public string? valor { get; set; }        // para "cip"
            public string? huellaBase64 { get; set; } // para "huella"
        }
        public sealed class IdReq { public int idPersonal { get; set; } }
        public sealed class MovimientoReq
        {
            public int idPersonal { get; set; }
            public string motivo { get; set; } = "";
            public string autoriza { get; set; } = "";
            public string? destino { get; set; }
        }
        public sealed class RetornoReq
        {
            public int idPersonal { get; set; }
            public string? novedades { get; set; }
        }

        // ===========================
        // ====== VISTA INICIO =======
        // ===========================
        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                return View("~/Views/Home/Index.cshtml");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al renderizar la página: {ex.Message}");
            }
        }

        // ====================================
        // ===== Buscar persona (CIP/huella) ===
        // ====================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuscarPersona([FromBody] BuscarPersonaRequest req)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.criterio))
                    return Json(new { success = false, message = "Selecciona un criterio: CIP o huella." });

                var criterio = req.criterio.Trim().ToLowerInvariant();
                if (criterio != "cip" && criterio != "huella")
                    return Json(new { success = false, message = "Criterio no soportado." });

                Personal? persona = null;

                // =========================
                //      BÚSQUEDA POR CIP
                // =========================
                if (criterio == "cip")
                {
                    // Normalización robusta del CIP
                    var raw = (req.valor ?? "").Trim();
                    var onlyDigits = new string(raw.Where(char.IsDigit).ToArray());

                    // Si trae más de 8 (con guiones/espacios), conservamos los últimos 8
                    if (onlyDigits.Length > 8)
                        onlyDigits = onlyDigits.Substring(onlyDigits.Length - 8);

                    // Rellenar con ceros a la izquierda (por si el front perdió ceros)
                    var cip = onlyDigits.PadLeft(8, '0');

                    if (cip.Length != 8 || !cip.All(char.IsDigit))
                        return Json(new { success = false, message = "CIP inválido (8 dígitos)." });

                    try
                    {
                        persona = await _db.Personal.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.NumeroCip == cip && p.Estado);
                    }
                    catch (Exception exQ)
                    {
                        return Json(new { success = false, message = "Error al consultar por CIP.", detail = exQ.Message });
                    }
                }
                // ============================
                //     BÚSQUEDA POR HUELLA
                // ============================
                // ============================
                //     BÚSQUEDA POR HUELLA
                // ============================
                else // huella
                {
                    if (string.IsNullOrWhiteSpace(req.huellaBase64))
                        return Json(new { success = false, message = "No se recibió la huella." });

                    byte[] captured;
                    try
                    {
                        var b64 = req.huellaBase64!;
                        var comma = b64.IndexOf(',');
                        if (comma >= 0) b64 = b64[(comma + 1)..];
                        captured = Convert.FromBase64String(b64);
                    }
                    catch
                    {
                        return Json(new { success = false, message = "Huella inválida (Base64)." });
                    }

                    // Hash de la huella capturada
                    var hCap = Sha256(captured);

                    // 🔹 OJO: nada de p.Huella.Any()
                    var candidatos = await _db.Personal.AsNoTracking()
                        .Where(p => p.Estado && p.Huella != null && p.Huella.Length > 0)
                        .Select(p => new
                        {
                            p.IdPersonal,
                            p.Grado,
                            p.Nombres,
                            p.Apellidos,
                            p.Unidad,
                            p.Foto,
                            p.Huella
                        })
                        .ToListAsync();  // desde aquí todo en memoria

                    var match = candidatos
                        .FirstOrDefault(p => Sha256(p.Huella!).SequenceEqual(hCap));

                    if (match != null)
                    {
                        persona = new Personal
                        {
                            IdPersonal = match.IdPersonal,
                            Grado = match.Grado,
                            Nombres = match.Nombres,
                            Apellidos = match.Apellidos,
                            Unidad = match.Unidad,
                            Foto = match.Foto,
                            Estado = true
                        };
                    }
                }


                if (persona == null)
                    return Json(new { success = false, message = "No se encontró personal con ese criterio." });

                var (startUtc, endUtc, _, nowStrLima) = TodayWindowUtcForLima();

                // ===== Asistencias de HOY =====
                var asistenciasHoy = await _db.Asistencia.AsNoTracking()
                    .Where(a => a.IdPersonal == persona.IdPersonal && a.FechaIngreso >= startUtc && a.FechaIngreso < endUtc)
                    .OrderBy(a => a.FechaIngreso)
                    .ToListAsync();

                var ciclos = asistenciasHoy.Select(a =>
                {
                    var eval = EvalLlegada(a.FechaIngreso);
                    return new
                    {
                        ingresoStr = ToLimaString(a.FechaIngreso),
                        salidaStr = a.FechaSalida.HasValue ? ToLimaString(a.FechaSalida.Value) : null,
                        abierta = !a.FechaSalida.HasValue,
                        llegoTarde = !eval.onTime,
                        minutosTarde = eval.minutesLate,
                        estadoLlegadaStr = eval.estadoLlegadaStr
                    };
                }).ToList();

                var yaTieneAlgunaHoy = ciclos.Count > 0;
                var asistenciaAbiertaHoy = ciclos.Any(c => c.abierta);

                // ===== Asistencias abiertas (cualquier día) =====
                var abiertas = await _db.Asistencia.AsNoTracking()
                    .Where(a => a.IdPersonal == persona.IdPersonal && a.FechaSalida == null)
                    .OrderByDescending(a => a.FechaIngreso)
                    .ToListAsync();

                var asistenciasAbiertas = abiertas.Select(a =>
                {
                    var eval = EvalLlegada(a.FechaIngreso);
                    return new
                    {
                        ingresoStr = ToLimaString(a.FechaIngreso),
                        ingresoUtcIso = DateTime.SpecifyKind(a.FechaIngreso, DateTimeKind.Utc).ToString("o"),
                        esDeHoy = (a.FechaIngreso >= startUtc && a.FechaIngreso < endUtc),
                        llegoTarde = !eval.onTime,
                        minutosTarde = eval.minutesLate,
                        minutosTemprano = eval.minutesEarly,
                        estadoLlegadaStr = eval.estadoLlegadaStr
                    };
                }).ToList();

                var anyAsistenciaAbierta = asistenciasAbiertas.Any();

                // ===== Movimientos pendientes =====
                var movsPend = await _db.Movimiento.AsNoTracking()
                    .Where(m => m.IdPersonal == persona.IdPersonal && m.FechaRetorno == null)
                    .OrderByDescending(m => m.FechaSalida)
                    .ToListAsync();

                var movimientosPendientes = movsPend.Select(m => new
                {
                    salidaStr = ToLimaString(m.FechaSalida),
                    salidaUtcIso = DateTime.SpecifyKind(m.FechaSalida, DateTimeKind.Utc).ToString("o"),
                    motivo = m.Motivo,
                    autoriza = m.Autoriza,
                    destino = string.IsNullOrWhiteSpace(m.Destino) ? "" : m.Destino
                }).ToList();

                var hayMovPend = movsPend.Any();

                // ===== Movimientos del DÍA =====
                var movsHoy = await _db.Movimiento.AsNoTracking()
                    .Where(m => m.IdPersonal == persona.IdPersonal
                             && m.FechaSalida >= startUtc && m.FechaSalida < endUtc)
                    .OrderBy(m => m.FechaSalida)
                    .ToListAsync();

                var movimientosHoy = movsHoy.Select(m => new
                {
                    salidaStr = ToLimaString(m.FechaSalida),
                    salidaUtcIso = DateTime.SpecifyKind(m.FechaSalida, DateTimeKind.Utc).ToString("o"),
                    retornoStr = m.FechaRetorno.HasValue ? ToLimaString(m.FechaRetorno.Value) : null,
                    retornoUtcIso = m.FechaRetorno.HasValue ? DateTime.SpecifyKind(m.FechaRetorno.Value, DateTimeKind.Utc).ToString("o") : null,
                    pendiente = !m.FechaRetorno.HasValue,
                    motivo = m.Motivo,
                    autoriza = m.Autoriza,
                    destino = string.IsNullOrWhiteSpace(m.Destino) ? "" : m.Destino
                }).ToList();

                // ===== Reglas UI =====
                var puedeRegistrarIngreso = !anyAsistenciaAbierta;
                var puedeRegistrarSalida = anyAsistenciaAbierta && !hayMovPend;
                var puedeRegistrarMovimiento = asistenciaAbiertaHoy && !hayMovPend;
                var puedeRegistrarRetornoMovimiento = hayMovPend;

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        id = persona.IdPersonal,
                        grado = persona.Grado,
                        nombres = persona.Nombres,
                        apellidos = persona.Apellidos,
                        unidad = persona.Unidad,
                        foto = string.IsNullOrWhiteSpace(persona.Foto)
                            ? Url.Content("~/Imagenes/sin-imagen.png")
                            : persona.Foto
                    },
                    ahora = nowStrLima,
                    yaTieneAlgunaHoy,
                    asistenciaAbiertaHoy,
                    puedeRegistrarIngreso,
                    puedeRegistrarSalida,
                    puedeRegistrarMovimiento,
                    puedeRegistrarRetornoMovimiento,
                    ciclos,
                    movimientosPendientes,
                    asistenciasAbiertas,
                    movimientosHoy,
                    anyAsistenciaAbierta
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al buscar persona: {ex.Message}" });
            }
        }

        // ==================================
        // ===== Registrar Asistencia (IN) ===
        // ==================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarAsistencia([FromBody] IdReq req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                var abierta = await _db.Asistencia.AsNoTracking()
                    .AnyAsync(a => a.IdPersonal == req.idPersonal && a.FechaSalida == null);
                if (abierta)
                    return Json(new { success = false, message = "Ya existe una asistencia abierta. Debe registrar su salida antes de una nueva." });

                var (_, _, nowUtc, _, nowLocal) = TodayWindowUtcForLimaWithLocal();

                var cutoff = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 7, 45, 0);
                var onTime = nowLocal <= cutoff;
                int minutesLate = 0, minutesEarly = 0;

                if (onTime) minutesEarly = (int)Math.Ceiling((cutoff - nowLocal).TotalMinutes);
                else minutesLate = (int)Math.Ceiling((nowLocal - cutoff).TotalMinutes);

                var asis = new Asistencia
                {
                    IdPersonal = req.idPersonal,
                    FechaIngreso = nowUtc,
                    Control_Asistencia = "Web"
                };

                _db.Asistencia.Add(asis);
                await _db.SaveChangesAsync();

                var horaStr = nowLocal.ToString("dd/MM/yyyy HH:mm:ss");
                var textoEstado = onTime ? "✅ Usted llegó a tiempo." : "⏰ Usted ha llegado tarde.";
                if (!onTime && minutesLate > 0) textoEstado += $" (+{minutesLate} min)";

                return Json(new
                {
                    success = true,
                    message = $"Asistencia registrada ({horaStr}). {textoEstado}",
                    onTime,
                    minutesLate,
                    minutesEarly,
                    horaStr
                });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "No se pudo registrar la asistencia. Verifica restricciones e intenta nuevamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar asistencia: {ex.Message}" });
            }
        }

        // ===================================
        // ===== Registrar Salida (OUT) ======
        // ===================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarSalida([FromBody] IdReq req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                var abierta = await _db.Asistencia
                    .Where(a => a.IdPersonal == req.idPersonal && a.FechaSalida == null)
                    .OrderByDescending(a => a.FechaIngreso)
                    .FirstOrDefaultAsync();

                if (abierta == null)
                    return Json(new { success = false, message = "No hay asistencia abierta." });

                abierta.FechaSalida = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Salida registrada." });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "No se pudo registrar la salida. Intenta nuevamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar salida: {ex.Message}" });
            }
        }

        // =========================================
        // ===== Registrar Movimiento (SALIDA) =====
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarMovimiento([FromBody] MovimientoReq req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });
                if (string.IsNullOrWhiteSpace(req.motivo) || string.IsNullOrWhiteSpace(req.autoriza))
                    return Json(new { success = false, message = "Motivo y Autoriza son obligatorios." });

                var (startUtc, endUtc, _, _) = TodayWindowUtcForLima();
                var hayAsisAbiertaHoy = await _db.Asistencia.AsNoTracking()
                    .AnyAsync(a => a.IdPersonal == req.idPersonal
                                && a.FechaIngreso >= startUtc && a.FechaIngreso < endUtc
                                && a.FechaSalida == null);
                if (!hayAsisAbiertaHoy)
                    return Json(new { success = false, message = "Se requiere asistencia abierta (hoy) para registrar movimiento." });

                var movPend = await _db.Movimiento.AsNoTracking()
                    .AnyAsync(m => m.IdPersonal == req.idPersonal && m.FechaRetorno == null);
                if (movPend)
                    return Json(new { success = false, message = "Ya existe un movimiento pendiente de retorno." });

                var mov = new Movimiento
                {
                    IdPersonal = req.idPersonal,
                    FechaSalida = DateTime.UtcNow,
                    Motivo = req.motivo.Trim(),
                    Autoriza = req.autoriza.Trim(),
                    Destino = string.IsNullOrWhiteSpace(req.destino) ? "" : req.destino.Trim(),
                    Novedades = ""
                };
                _db.Movimiento.Add(mov);
                await _db.SaveChangesAsync();

                return Json(new { success = true, id = mov.IdMovimiento });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "No se pudo registrar el movimiento. Revisa los datos e intenta nuevamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar movimiento: {ex.Message}" });
            }
        }

        // ============================================
        // ===== Registrar Retorno de Movimiento ======
        // ============================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRetornoMovimiento([FromBody] RetornoReq req)
        {
            try
            {
                if (req == null || req.idPersonal <= 0)
                    return Json(new { success = false, message = "Personal no válido." });

                var mov = await _db.Movimiento
                    .Where(m => m.IdPersonal == req.idPersonal && m.FechaRetorno == null)
                    .OrderByDescending(m => m.FechaSalida)
                    .FirstOrDefaultAsync();

                if (mov == null)
                    return Json(new { success = false, message = "No hay movimiento pendiente de retorno." });

                mov.FechaRetorno = DateTime.UtcNow;
                mov.Novedades = (req.novedades ?? "").Trim();

                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "No se pudo registrar el retorno del movimiento." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar retorno: {ex.Message}" });
            }
        }
    }
}
