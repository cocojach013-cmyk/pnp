using System.Threading;
using System.Threading.Tasks;

namespace ControlPNP.Services
{
    public interface IBiometriaService
    {
        /// <summary>
        /// Prueba el lector: inicializa SDK, cuenta dispositivos y lee width/height.
        /// </summary>
        Task<BiometriaTestResult> ProbarLectorAsync(CancellationToken ct = default);

        /// <summary>
        /// Captura una huella en modo kiosco y devuelve el template en Base64.
        /// </summary>
        Task<string?> CapturarBase64Async(CancellationToken ct = default);
    }
}
