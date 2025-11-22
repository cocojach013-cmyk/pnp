using System.Threading.Tasks;

namespace ControlPNP.Services
{
    public interface IAuditoriaWriter
    {
        Task LogAsync(string accion, string? registroId, string? descripcion, System.DateTime? inicioSesionUtc = null);
    }
}
