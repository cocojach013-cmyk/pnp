using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ControlPNP.Utils
{
    public static class AuditDiff
    {
        /// Compara propiedades públicas simples y devuelve "Campo: 'old' -> 'new'".
        public static string BuildDiff(object? original, object? current, params string[] onlyProps)
        {
            if (original == null && current == null) return "(sin cambios)";
            var type = (original ?? current)!.GetType();

            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                            .Where(p =>
                            {
                                // Excluir binarios y muy largos
                                if (p.PropertyType == typeof(byte[]) || p.PropertyType == typeof(byte)) return false;
                                if (onlyProps?.Length > 0) return onlyProps.Contains(p.Name);
                                return true;
                            })
                            .ToList();

            var changes = new List<string>();
            foreach (var p in props)
            {
                var ov = original == null ? null : p.GetValue(original);
                var nv = current == null ? null : p.GetValue(current);

                // Normaliza fechas para legibilidad (local Lima se arma en el Controller si se desea)
                string sval(object? v)
                {
                    if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                    return v?.ToString() ?? "(null)";
                }

                var so = sval(ov);
                var sn = sval(nv);
                if (!string.Equals(so, sn, StringComparison.Ordinal))
                {
                    changes.Add($"{p.Name}: '{so}' -> '{sn}'");
                }
            }
            return changes.Count == 0 ? "(sin cambios)" : string.Join("; ", changes);
        }
    }
}
