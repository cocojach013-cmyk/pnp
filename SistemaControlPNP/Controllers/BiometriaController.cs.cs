using System;
using System.Threading;
using System.Threading.Tasks;
using ControlPNP.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPNP.Controllers
{
    /// <summary>
    /// Controlador de BIOMETRÍA en modo kiosco.
    /// NOTA: Solo funciona si el lector de huella está conectado
    /// al mismo equipo donde corre la aplicación (servidor/kiosco).
    /// No puede acceder al USB del navegador del cliente.
    /// </summary>
    [Authorize]
    [Route("[controller]/[action]")]
    public class BiometriaController : Controller
    {
        private readonly IBiometriaService _biometria;

        public BiometriaController(IBiometriaService biometria)
        {
            _biometria = biometria ?? throw new ArgumentNullException(nameof(biometria));
        }

        [HttpGet]
        public IActionResult TestCaptura()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Probe()
        {
            return Ok(new
            {
                ok = true,
                ts = DateTime.UtcNow
            });
        }

        [HttpGet]
        public async Task<IActionResult> TestLector()
        {
            try
            {
                var result = await _biometria.ProbarLectorAsync();

                return Json(new
                {
                    success = result.Ok,
                    message = result.Mensaje,
                    dispositivos = result.Dispositivos,
                    width = result.Width,
                    height = result.Height
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al probar el lector: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Captura en MODO KIOSCO (lector conectado al servidor).
        /// Devuelve el template de la huella en Base64.
        /// POST: /Biometria/CapturarKiosco
        /// </summary>
        [AllowAnonymous] // 👈 para que funcione desde la vista pública de consulta
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CapturarKiosco()
        {
            try
            {
                // Timeout defensivo para evitar que el SDK se cuelgue
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

                var dataB64 = await _biometria.CapturarBase64Async(cts.Token);

                if (string.IsNullOrWhiteSpace(dataB64))
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo capturar la huella (modo kiosco)."
                    });
                }

                // 👇 Mantenemos huellaBase64 (REGISTRO) y añadimos base64 (BUSCAR)
                return Json(new
                {
                    success = true,
                    base64 = dataB64,
                    huellaBase64 = dataB64
                });
            }
            catch (OperationCanceledException)
            {
                return Json(new
                {
                    success = false,
                    message = "La captura de huella excedió el tiempo de espera."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al capturar huella (modo kiosco): " + ex.Message
                });
            }
        }
    }
}
