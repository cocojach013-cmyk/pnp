using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using ControlPNP.Data;
using ControlPNP.ViewModels;

namespace ControlPNP.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ControlDBContext _db;

        public DashboardController(ControlDBContext db)
        {
            _db = db;
        }

        // ===== Helpers zona horaria =====
        private static TimeZoneInfo GetLimaTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static (DateTime startUtc, DateTime endUtc, DateTime nowUtc) TodayWindowUtcForLima()
        {
            var tz = GetLimaTz();
            var nowUtc = DateTime.UtcNow;
            var nowLima = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var startLima = nowLima.Date;
            var endLima = startLima.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLima, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLima, tz);
            return (startUtc, endUtc, nowUtc);
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var (hoyUtc, manianaUtc, ahoraUtc) = TodayWindowUtcForLima();

                var vm = new DashboardVM
                {
                    PersonalActivos = await _db.Personal.CountAsync(p => p.Estado),
                    UsuariosActivos = await _db.Usuarios.CountAsync(u => u.Estado),
                    AsistenciasHoy = await _db.Asistencia.CountAsync(a => a.FechaIngreso >= hoyUtc && a.FechaIngreso < manianaUtc),
                    MovimientosAbiertos = await _db.Movimiento.CountAsync(m => m.FechaRetorno == null || m.FechaRetorno > ahoraUtc),
                    UltimasAsistencias = await _db.Asistencia
                        .AsNoTracking()
                        .Include(a => a.Personal)
                        .OrderByDescending(a => a.FechaIngreso)
                        .Take(10)
                        .Select(a => new UltimaAsistenciaVM
                        {
                            Ingreso = a.FechaIngreso,
                            Salida = a.FechaSalida,
                            PersonalNombre = ((a.Personal.Nombres ?? "") + " " + (a.Personal.Apellidos ?? "")).Trim(),
                            Unidad = a.Personal.Unidad
                        })
                        .ToListAsync(),
                    UltimosMovimientos = await _db.Movimiento
                        .AsNoTracking()
                        .Include(m => m.Personal)
                        .OrderByDescending(m => m.FechaSalida)
                        .Take(10)
                        .Select(m => new UltimoMovimientoVM
                        {
                            Salida = m.FechaSalida,
                            Retorno = m.FechaRetorno,
                            Motivo = m.Motivo,
                            Destino = m.Destino,
                            PersonalNombre = ((m.Personal.Nombres ?? "") + " " + (m.Personal.Apellidos ?? "")).Trim()
                        })
                        .ToListAsync(),
                    UltimasAcciones = await _db.Auditoria
                        .AsNoTracking()
                        .Include(a => a.UsuarioRef)
                        .OrderByDescending(a => a.FechaHora)
                        .Take(8)
                        .Select(a => new ActividadVM
                        {
                            FechaHora = a.FechaHora,
                            Accion = a.Accion,
                            Descripcion = a.Descripcion,
                            Usuario = a.UsuarioRef != null ? a.UsuarioRef.UsuarioLogin : "(desconocido)",
                            Ip = a.IpUsuario
                        })
                        .ToListAsync()
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al cargar el dashboard: {ex.Message}");
            }
        }

        // ===== AYUDA =====
        [HttpGet]
        public IActionResult Ayuda()
        {
            try
            {
                // No necesita modelo por ahora
                return View();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al cargar la ayuda: {ex.Message}");
            }
        }

        [AllowAnonymous]
        public IActionResult Error()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al mostrar la vista de error: {ex.Message}");
            }
        }
    }
}
