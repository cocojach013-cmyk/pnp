using System;
using System.Threading.Tasks;
using ControlPNP.Data;
using SistemaControlPNP.Models;

namespace ControlPNP.Services
{
    public sealed class AuditoriaWriter : IAuditoriaWriter
    {
        private readonly ControlPNP.Data.ControlDBContext _db;
        private readonly IRequestContext _ctx;

        public AuditoriaWriter(ControlPNP.Data.ControlDBContext db, IRequestContext ctx)
        {
            _db = db;
            _ctx = ctx;
        }

        public async Task LogAsync(string accion, string? registroId, string? descripcion, DateTime? inicioSesionUtc = null)
        {
            var a = new Auditoria
            {
                IdUsuario = _ctx.CurrentUserId(),
                Accion = accion,
                RegistroID = registroId,
                Descripcion = descripcion,
                IpUsuario = _ctx.ClientIp(),
                FechaHora = DateTime.UtcNow,
                InicioSesion = inicioSesionUtc
            };

            _db.Auditoria.Add(a);
            await _db.SaveChangesAsync();
        }
    }
}
