using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace ControlPNP.Infrastructure
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Devuelve la IP del cliente considerando proxies y encabezados estándar.
        /// Prioriza: RFC 7239 Forwarded → X-Real-IP → X-Forwarded-For → RemoteIpAddress.
        /// Elige la primera IP enrutable (no loopback/privada/link-local).
        /// </summary>
        public static string GetClientIp(this HttpContext http)
        {
            if (http == null) return string.Empty;

            // 1) Forwarded: for=...
            // Ejemplos: Forwarded: for=192.0.2.60; proto=http; by=203.0.113.43
            //           Forwarded: for="[2001:db8:cafe::17]:4711"
            if (http.Request.Headers.TryGetValue("Forwarded", out var fwdValues))
            {
                foreach (var header in fwdValues)
                {
                    var parts = header.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        var kv = p.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (kv.Length == 2 && kv[0].Trim().Equals("for", StringComparison.OrdinalIgnoreCase))
                        {
                            var raw = kv[1].Trim().Trim('"');
                            var host = raw;

                            // Remove brackets/port for IPv6 like [::1]:4711
                            if (host.StartsWith("[") && host.Contains("]"))
                            {
                                host = host.Substring(1, host.IndexOf(']') - 1);
                            }
                            else
                            {
                                // Strip :port if present in IPv4/hostname
                                var colon = host.LastIndexOf(':');
                                if (colon > 0 && host.IndexOf(':') == colon) // single colon → likely :port
                                    host = host.Substring(0, colon);
                            }

                            if (TryNormalizeRoutableIp(host, out var ip))
                                return ip;
                        }
                    }
                }
            }

            // 2) X-Real-IP
            if (http.Request.Headers.TryGetValue("X-Real-IP", out var realIpValues))
            {
                foreach (var raw in realIpValues)
                    if (TryNormalizeRoutableIp(raw, out var ip))
                        return ip;
            }

            // 3) X-Forwarded-For (lista separada por coma)
            if (http.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues))
            {
                foreach (var rawList in xffValues)
                {
                    foreach (var raw in rawList.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                        if (TryNormalizeRoutableIp(raw, out var ip))
                            return ip;
                }
            }

            // 4) RemoteIpAddress
            var r = http.Connection.RemoteIpAddress;
            if (r != null)
            {
                var norm = NormalizeIp(r);
                if (!string.IsNullOrEmpty(norm))
                    return norm;
            }

            return string.Empty;
        }

        private static bool TryNormalizeRoutableIp(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (IPAddress.TryParse(raw, out var ip))
            {
                if (IsRoutable(ip))
                {
                    normalized = NormalizeIp(ip);
                    return !string.IsNullOrEmpty(normalized);
                }
                return false;
            }

            // Si no es IP parseable, descartar (evita hostnames)
            return false;
        }

        private static string NormalizeIp(IPAddress ip)
        {
            // Mapea IPv6 ::ffff:127.0.0.1 a 127.0.0.1
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            // Normaliza loopback a 127.0.0.1
            if (IPAddress.IsLoopback(ip)) return "127.0.0.1";

            return ip.ToString();
        }

        private static bool IsRoutable(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return false;

            // Privadas/reservadas
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return false;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                // 169.254.0.0/16 (link-local)
                if (bytes[0] == 169 && bytes[1] == 254) return false;
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv4MappedToIPv6) return IsRoutable(ip.MapToIPv4());
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return false;
            }

            return true;
        }
    }
}
