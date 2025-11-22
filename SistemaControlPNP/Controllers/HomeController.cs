using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using ControlPNP.Data;
using SistemaControlPNP.Models;
using ControlPNP.Filters;

namespace ControlPNP.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly ControlDBContext _db;

        public HomeController(ControlDBContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al renderizar la vista: {ex.Message}");
            }
        }

        // ===== DTOs =====
        public sealed class BuscarPersonaRequest
        {
            public string? criterio { get; set; }     // "cip" | "huella"
            public string? valor { get; set; }        // para "cip"
            public string? huellaBase64 { get; set; } // para "huella"
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("HOME_BUSCAR_PERSONA", registroKey: "criterio")]
        public async Task<IActionResult> BuscarPersona([FromBody] BuscarPersonaRequest req)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.criterio))
                    return Json(new { success = false, message = "Selecciona un criterio: cip o huella." });

                var criterio = req.criterio.Trim().ToLowerInvariant();
                if (criterio != "cip" && criterio != "huella")
                    return Json(new { success = false, message = "Criterio no soportado. Usa: cip o huella." });

                Personal? persona = null;

                if (criterio == "cip")
                {
                    var cip = (req.valor ?? "").Trim();
                    if (cip.Length != 8 || !cip.All(char.IsDigit))
                        return Json(new { success = false, message = "CIP inválido (8 dígitos)." });

                    persona = await _db.Personal.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.NumeroCip == cip && p.Estado);
                }
                else // huella
                {
                    if (string.IsNullOrWhiteSpace(req.huellaBase64))
                        return Json(new { success = false, message = "No se recibió la huella capturada." });

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

                    var capturedHash = Sha256(captured);

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
                        .ToListAsync();

                    var match = candidatos.FirstOrDefault(p => Sha256(p.Huella!).SequenceEqual(capturedHash));
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

                var (startUtc, endUtc, nowUtc, nowStrLima) = TodayWindowUtcForLima();

                var asistenciasHoy = await _db.Asistencia.AsNoTracking()
                    .Where(a => a.IdPersonal == persona.IdPersonal
                             && a.FechaIngreso >= startUtc
                             && a.FechaIngreso < endUtc)
                    .OrderBy(a => a.FechaIngreso)
                    .ToListAsync();

                var ciclos = asistenciasHoy.Select(a => new
                {
                    ingresoStr = ToLimaString(a.FechaIngreso),
                    salidaStr = a.FechaSalida.HasValue ? ToLimaString(a.FechaSalida.Value) : null,
                    abierta = !a.FechaSalida.HasValue
                }).ToList();

                var yaTieneAlgunaHoy = ciclos.Count > 0;
                var hayAbierta = ciclos.Any(c => c.abierta);

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
                    puedeRegistrarIngreso = !hayAbierta,
                    puedeRegistrarSalida = hayAbierta,
                    puedeRegistrarMovimiento = hayAbierta,
                    ciclos
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al buscar persona: {ex.Message}" });
            }
        }

        // ===== Helpers (zona horaria Lima) =====
        private static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static (DateTime startUtc, DateTime endUtc, DateTime nowUtc, string nowStrLima) TodayWindowUtcForLima()
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

        private static string ToLimaString(DateTime utcInstant)
        {
            var tz = GetLimaTz();
            var lima = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), tz);
            return lima.ToString("dd/MM/yyyy HH:mm:ss");
        }

        private static byte[] Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(data);
        }
    }
}
