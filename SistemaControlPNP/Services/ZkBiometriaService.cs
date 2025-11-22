using System;
using System.Threading;
using System.Threading.Tasks;
using libzkfpcsharp;

namespace ControlPNP.Services
{
    /// <summary>
    /// Implementación de biometría usando ZKFinger (libzkfpcsharp.dll).
    /// Diseñado para MODO KIOSCO (lector conectado al servidor).
    /// </summary>
    public class ZkBiometriaService : IBiometriaService
    {
        // Para evitar problemas de concurrencia con el SDK nativo
        private static readonly object _lock = new object();

        // =============================================================
        //  MÉTODO 1 — PROBAR LECTOR  (ESTE YA FUNCIONA BIEN)
        // =============================================================
        public Task<BiometriaTestResult> ProbarLectorAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    var result = new BiometriaTestResult();
                    IntPtr devHandle = IntPtr.Zero;

                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        int ret = zkfp2.Init();     // 0 = OK
                        if (ret != 0)
                        {
                            result.Ok = false;
                            result.Mensaje = $"Init() falló. Código: {ret}";
                            return result;
                        }

                        int devCount = zkfp2.GetDeviceCount();
                        result.Dispositivos = devCount;

                        if (devCount <= 0)
                        {
                            result.Ok = false;
                            result.Mensaje = "No se detecta ningún lector conectado.";
                            zkfp2.Terminate();
                            return result;
                        }

                        devHandle = zkfp2.OpenDevice(0);
                        if (devHandle == IntPtr.Zero)
                        {
                            result.Ok = false;
                            result.Mensaje = "No se pudo abrir el lector (OpenDevice).";
                            zkfp2.Terminate();
                            return result;
                        }

                        byte[] param = new byte[4];
                        int size = 4;
                        int width = 0, height = 0;

                        // 1 = width
                        zkfp2.GetParameters(devHandle, 1, param, ref size);
                        zkfp2.ByteArray2Int(param, ref width);

                        // 2 = height
                        size = 4;
                        param = new byte[4];
                        zkfp2.GetParameters(devHandle, 2, param, ref size);
                        zkfp2.ByteArray2Int(param, ref height);

                        result.Width = width;
                        result.Height = height;
                        result.Ok = true;
                        result.Mensaje = "Lector OK.";
                    }
                    catch (Exception ex)
                    {
                        result.Ok = false;
                        result.Mensaje = "Excepción en ProbarLectorAsync: " + ex.Message;
                    }
                    finally
                    {
                        try
                        {
                            if (devHandle != IntPtr.Zero)
                                zkfp2.CloseDevice(devHandle);
                        }
                        catch { }

                        try { zkfp2.Terminate(); } catch { }
                    }

                    return result;
                }
            }, ct);
        }

        // =============================================================
        //  MÉTODO 2 — CAPTURAR HUELLA (COMPLETO Y NUEVO)
        // =============================================================
        public Task<string?> CapturarBase64Async(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    IntPtr devHandle = IntPtr.Zero;

                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        int ret = zkfp2.Init();
                        if (ret != 0)
                            throw new Exception($"Init() falló. Código: {ret}");

                        int devCount = zkfp2.GetDeviceCount();
                        if (devCount <= 0)
                            throw new Exception("No se detecta ningún lector conectado.");

                        devHandle = zkfp2.OpenDevice(0);
                        if (devHandle == IntPtr.Zero)
                            throw new Exception("No se pudo abrir el lector (OpenDevice).");

                        // Obtener width / height
                        byte[] param = new byte[4];
                        int size = 4;
                        int width = 0, height = 0;

                        zkfp2.GetParameters(devHandle, 1, param, ref size);
                        zkfp2.ByteArray2Int(param, ref width);

                        size = 4;
                        param = new byte[4];
                        zkfp2.GetParameters(devHandle, 2, param, ref size);
                        zkfp2.ByteArray2Int(param, ref height);

                        if (width <= 0 || height <= 0)
                            throw new Exception("Dimensiones reportadas inválidas.");

                        // Buffers
                        byte[] imgBuffer = new byte[width * height];
                        int tmplSize = 2048;
                        byte[] template = new byte[tmplSize];

                        // Intentos para capturar
                        const int MAX_INTENTOS = 40; // ~8 segundos
                        ret = -1;

                        for (int i = 0; i < MAX_INTENTOS; i++)
                        {
                            ct.ThrowIfCancellationRequested();

                            ret = zkfp2.AcquireFingerprint(devHandle, imgBuffer, template, ref tmplSize);
                            if (ret == 0)
                                break;

                            Thread.Sleep(200);
                        }

                        if (ret != 0)
                            throw new Exception($"No se pudo capturar la huella. Código SDK: {ret}");

                        // Ajustar tamaño del template
                        Array.Resize(ref template, tmplSize);

                        // Convertir a Base64
                        string base64 = Convert.ToBase64String(template);
                        return base64;
                    }
                    finally
                    {
                        try
                        {
                            if (devHandle != IntPtr.Zero)
                                zkfp2.CloseDevice(devHandle);
                        }
                        catch { }

                        try { zkfp2.Terminate(); } catch { }
                    }
                }
            }, ct);
        }
    }
}
