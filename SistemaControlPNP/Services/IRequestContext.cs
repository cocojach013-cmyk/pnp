using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ControlPNP.Services
{
    public interface IRequestContext
    {
        int? CurrentUserId();
        string? CurrentUsername();
        string ClientIp();
    }

    public sealed class RequestContext : IRequestContext
    {
        private readonly IHttpContextAccessor _http;

        public RequestContext(IHttpContextAccessor http) { _http = http; }

        public int? CurrentUserId()
        {
            var id = _http.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(id, out var x) ? x : null;
        }

        public string? CurrentUsername()
            => _http.HttpContext?.User?.FindFirst("Usuario")?.Value
            ?? _http.HttpContext?.User?.Identity?.Name;

        public string ClientIp()
        {
            var ctx = _http.HttpContext;
            if (ctx == null) return "N/A";

            string? xf = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xf))
            {
                // Puede traer varias IP separadas por coma (cliente, proxies…)
                var first = xf.Split(',').Select(s => s.Trim()).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }

            string? xr = ctx.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xr)) return xr;

            return ctx.Connection?.RemoteIpAddress?.ToString() ?? "N/A";
        }
    }
}
