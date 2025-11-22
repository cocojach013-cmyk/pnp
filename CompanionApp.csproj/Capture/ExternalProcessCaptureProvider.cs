using System.Diagnostics;

namespace CompanionApp.Capture
{
    public class ExternalProcessCaptureProvider : ICaptureProvider
    {
        private readonly IConfiguration _cfg;
        public ExternalProcessCaptureProvider(IConfiguration cfg) => _cfg = cfg;

        public async Task<string> CaptureBase64Async(CancellationToken ct = default)
        {
            var fileName = _cfg["Companion:Capture:ExternalProcess:FileName"] ?? throw new InvalidOperationException("ExternalProcess FileName no configurado.");
            var args = _cfg["Companion:Capture:ExternalProcess:Arguments"] ?? "";
            var timeout = int.TryParse(_cfg["Companion:Capture:ExternalProcess:TimeoutMs"], out var t) ? t : 10000;

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);

            if (!proc.WaitForExit(timeout))
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                throw new TimeoutException("La captura por proceso externo superó el tiempo límite.");
            }

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"CLI biométrico devolvió código {proc.ExitCode}: {stderr}");

            // Se espera que stdout ya sea un data URI o un plain base64
            var output = (stdout ?? "").Trim();
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("El proceso externo no devolvió datos.");

            // Si no viene con 'data:', construimos un data URI por defecto:
            if (!output.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                output = $"data:application/octet-stream;base64,{output}";

            return output;
        }
    }
}
