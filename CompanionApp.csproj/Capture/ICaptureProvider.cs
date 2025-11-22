namespace CompanionApp.Capture
{
    public interface ICaptureProvider
    {
        /// <summary>
        /// Debe devolver un data URI con base64
        ///   p.ej. "data:application/octet-stream;base64,AAAA..."
        /// Puede ser TEMPLATE (ISO/ANSI) o imagen WSQ/PNG.
        /// </summary>
        Task<string> CaptureBase64Async(CancellationToken ct = default);
    }
}
