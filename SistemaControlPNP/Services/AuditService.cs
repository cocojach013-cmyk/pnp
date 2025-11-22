using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ControlPNP.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;

namespace ControlPNP.Services
{
    public class AuditService : IAuditService
    {
        private readonly ControlDBContext _db;
        private readonly IHttpContextAccessor _http;

        public AuditService(ControlDBContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public async Task LogAsync(string accion, string? registroId = null, string? descripcion = null)
        {
            try
            {
                var ctx = _http.HttpContext;
                var userIdStr = ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int? idUsuario = int.TryParse(userIdStr, out var id) ? id : (int?)null;

                var ent = new Auditoria
                {
                    IdUsuario = idUsuario,
                    Accion = accion ?? "",
                    RegistroID = string.IsNullOrWhiteSpace(registroId) ? null : registroId,
                    Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion,
                    IpUsuario = GetClientIp(ctx),
                    FechaHora = DateTime.UtcNow
                };

                _db.Auditoria.Add(ent);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // nunca romper el flujo por auditoría
            }
        }

        public async Task LogSigninAsync(DateTime inicioSesionLocal, string? descripcion = null)
        {
            try
            {
                var ctx = _http.HttpContext;
                var userIdStr = ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int? idUsuario = int.TryParse(userIdStr, out var id) ? id : (int?)null;

                DateTime? inicioSesionUtc = null;
                try
                {
                    // asume que inicioSesionLocal viene en hora de Lima (UTC-5, sin DST)
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
                    var unspecified = DateTime.SpecifyKind(inicioSesionLocal, DateTimeKind.Unspecified);
                    inicioSesionUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
                }
                catch { }

                var ent = new Auditoria
                {
                    IdUsuario = idUsuario,
                    Accion = "LOGIN",
                    RegistroID = null,
                    Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion,
                    IpUsuario = GetClientIp(ctx),
                    FechaHora = DateTime.UtcNow,
                    InicioSesion = inicioSesionUtc
                };

                _db.Auditoria.Add(ent);
                await _db.SaveChangesAsync();
            }
            catch { }
        }

        private static string? GetClientIp(HttpContext? ctx)
        {
            if (ctx == null) return null;
            var ip = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                var first = ip.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            return ctx.Connection?.RemoteIpAddress?.ToString();
        }
    }
}
