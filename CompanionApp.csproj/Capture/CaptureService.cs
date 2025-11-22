using CompanionApp.Models;

namespace CompanionApp.Capture
{
    public class CaptureService
    {
        private readonly ICaptureProvider _provider;
        public CaptureService(ICaptureProvider provider) => _provider = provider;

        public async Task<CaptureResult> CaptureAsync(CancellationToken ct = default)
        {
            var b64 = await _provider.CaptureBase64Async(ct);
            return new CaptureResult { success = true, base64 = b64 };
        }
    }
}
