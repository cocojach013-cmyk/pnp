using System.Text;

namespace CompanionApp.Capture
{
    public class DemoCaptureProvider : ICaptureProvider
    {
        public Task<string> CaptureBase64Async(CancellationToken ct = default)
        {
            // Devuelve "ABC" como bytes, solo para probar flujo end-to-end:
            var dummy = Convert.ToBase64String(Encoding.UTF8.GetBytes("ABC"));
            var dataUri = $"data:application/octet-stream;base64,{dummy}";
            return Task.FromResult(dataUri);
        }
    }
}
