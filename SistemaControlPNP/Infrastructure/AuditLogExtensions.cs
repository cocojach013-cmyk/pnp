using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ControlPNP.Data;
using Microsoft.AspNetCore.Http;
using SistemaControlPNP.Models;

namespace ControlPNP.Infrastructure
{
    public static class AuditLogExtensions
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 6
        };

        /// <summary>
        /// Registra una línea en Auditoria usando el DbContext del request.
        /// </summary>
        public static async Task RegistrarAuditoriaAsync(
            this ControlDBContext db,
            HttpContext http,
            int idUsuario,
            string accion,
            string? registroId,
            object? payload = null,
            DateTime? inicioSesionUtc = null,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            db.Auditoria.Add(new Auditoria
            {
                IdUsuario = idUsuario,
                Accion = accion,
                RegistroID = registroId,
                Descripcion = payload == null ? null : SafeSerialize(payload),
                IpUsuario = http.GetClientIp(),
                FechaHora = now,
                InicioSesion = inicioSesionUtc
            });

            await db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Calcula diferencias simples entre objetos del mismo tipo.
        /// Soporta primitivos, string, bool, enum, DateTime/Offset, int/long/decimal/double y nullables.
        /// </summary>
        public static Dictionary<string, (string? Old, string? New)> Diff<T>(T? oldObj, T? newObj)
        {
            var dict = new Dictionary<string, (string?, string?)>();
            if (oldObj is null || newObj is null) return dict;

            var props = typeof(T).GetProperties()
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p =>
                {
                    var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    return t.IsPrimitive
                           || t.IsEnum
                           || t == typeof(string)
                           || t == typeof(DateTime)
                           || t == typeof(DateTimeOffset)
                           || t == typeof(bool)
                           || t == typeof(int) || t == typeof(long)
                           || t == typeof(decimal) || t == typeof(double) || t == typeof(float);
                });

            foreach (var p in props)
            {
                var oldVal = p.GetValue(oldObj);
                var newVal = p.GetValue(newObj);
                if (!Equals(oldVal, newVal))
                {
                    dict[p.Name] = (ToInvariantString(oldVal), ToInvariantString(newVal));
                }
            }

            return dict;
        }

        private static string SafeSerialize(object obj)
            => JsonSerializer.Serialize(obj, JsonOpts);

        private static string? ToInvariantString(object? val)
        {
            if (val is null) return null;

            var t = val.GetType();
            var u = Nullable.GetUnderlyingType(t) ?? t;

            if (u.IsEnum) return Convert.ToInt64(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            if (u == typeof(DateTime))
                return ((DateTime)val).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            if (u == typeof(DateTimeOffset))
                return ((DateTimeOffset)val).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            if (u == typeof(decimal) || u == typeof(double) || u == typeof(float))
                return Convert.ToString(val, CultureInfo.InvariantCulture);

            return val.ToString();
        }
    }
}
