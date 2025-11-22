using System;
using System.Threading.Tasks;

namespace ControlPNP.Services
{
    public interface IAuditService
    {
        Task LogAsync(string accion, string? registroId = null, string? descripcion = null);
        Task LogSigninAsync(DateTime inicioSesionLocal, string? descripcion = null);
    }
}
