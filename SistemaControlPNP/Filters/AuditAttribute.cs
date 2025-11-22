using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ControlPNP.Data;
using ControlPNP.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaControlPNP.Models;

namespace ControlPNP.Filters
{
    /// <summary>
    /// Uso:
    /// [Audit("PERSONAL_CREAR",   registroKey: "NumeroCip")]
    /// [Audit("PERSONAL_EDITAR",  registroKey: "IdPersonal")]
    /// [Audit("PERSONAL_ACTIVAR", registroKey: "id")]
    /// [Audit("PERSONAL_DESACTIVAR", registroKey: "id")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AuditAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _accion;
        private readonly string _registroKey;

        public AuditAttribute(string accion, string registroKey)
        {
            _accion = accion ?? throw new ArgumentNullException(nameof(accion));
            _registroKey = registroKey ?? throw new ArgumentNullException(nameof(registroKey));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            // Usa el mismo DbContext scoped del request
            var db = http.RequestServices.GetRequiredService<ControlDBContext>();

            // Usuario (claim NameIdentifier)
            int.TryParse(
                http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? http.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value,
                out var userId);

            // Obtener registroId (por route, arg, form o query)
            string? registroId = ResolveRegistroId(context, _registroKey);

            // Capturar BEFORE si aplica a PERSONAL_* con Id
            Personal? before = null;
            bool isPersonalAction = _accion.StartsWith("PERSONAL_", StringComparison.OrdinalIgnoreCase);

            if (isPersonalAction && !string.IsNullOrWhiteSpace(registroId))
            {
                if (_accion.Contains("EDITAR", StringComparison.OrdinalIgnoreCase) ||
                    _accion.Contains("ACTIVAR", StringComparison.OrdinalIgnoreCase) ||
                    _accion.Contains("DESACTIVAR", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(registroId, out var idPers))
                        before = await db.Personal.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersonal == idPers);
                }
            }

            // Ejecutar acción
            var executed = await next();

            // ¿Fue exitosa?
            bool ok = WasSuccessful(executed);

            // Construir payload
            object? payload;

            if (isPersonalAction)
            {
                if (_accion.Contains("CREAR", StringComparison.OrdinalIgnoreCase))
                {
                    // registroKey = NumeroCip → buscar insertado
                    Personal? created = null;
                    if (!string.IsNullOrWhiteSpace(registroId))
                        created = await db.Personal.AsNoTracking().FirstOrDefaultAsync(p => p.NumeroCip == registroId);

                    if (created != null)
                        registroId = created.IdPersonal.ToString();

                    payload = new
                    {
                        Target = "Personal",
                        IdPersonal = created?.IdPersonal,
                        CIP = created?.NumeroCip,
                        DNI = created?.Dni,
                        Apellidos = created?.Apellidos,
                        Nombres = created?.Nombres,
                        Unidad = created?.Unidad,
                        Estado = created?.Estado
                    };
                }
                else if (_accion.Contains("EDITAR", StringComparison.OrdinalIgnoreCase))
                {
                    Personal? after = null;
                    if (int.TryParse(registroId, out var idPers))
                        after = await db.Personal.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersonal == idPers);

                    var cambios = AuditLogExtensions.Diff(before, after);

                    payload = new
                    {
                        Target = "Personal",
                        IdPersonal = after?.IdPersonal ?? before?.IdPersonal,
                        Before = before == null ? null : new
                        {
                            before.NumeroCip,
                            before.Dni,
                            before.Apellidos,
                            before.Nombres,
                            before.Unidad,
                            before.CorreoInstitucional,
                            before.Sexo,
                            before.FechaNacimiento,
                            before.Estado
                        },
                        After = after == null ? null : new
                        {
                            after.NumeroCip,
                            after.Dni,
                            after.Apellidos,
                            after.Nombres,
                            after.Unidad,
                            after.CorreoInstitucional,
                            after.Sexo,
                            after.FechaNacimiento,
                            after.Estado
                        },
                        Cambios = cambios
                    };
                }
                else if (_accion.Contains("ACTIVAR", StringComparison.OrdinalIgnoreCase) ||
                         _accion.Contains("DESACTIVAR", StringComparison.OrdinalIgnoreCase))
                {
                    Personal? after = null;
                    if (int.TryParse(registroId, out var idPers))
                        after = await db.Personal.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersonal == idPers);

                    payload = new
                    {
                        Target = "Personal",
                        IdPersonal = after?.IdPersonal ?? before?.IdPersonal,
                        CIP = after?.NumeroCip ?? before?.NumeroCip,
                        DNI = after?.Dni ?? before?.Dni,
                        Apellidos = after?.Apellidos ?? before?.Apellidos,
                        Nombres = after?.Nombres ?? before?.Nombres,
                        EstadoAntes = before?.Estado,
                        EstadoDespues = after?.Estado
                    };
                }
                else
                {
                    payload = new { Target = "Personal", RegistroID = registroId };
                }
            }
            else
            {
                payload = new { RegistroID = registroId };
            }

            // Guardar auditoría solo en éxito y con usuario válido
            if (ok && userId > 0)
            {
                await db.RegistrarAuditoriaAsync(
                    http,
                    userId,
                    _accion,
                    registroId,
                    payload
                );
            }
        }

        private static string? ResolveRegistroId(ActionExecutingContext ctx, string key)
        {
            // route values
            if (ctx.RouteData.Values.TryGetValue(key, out var routeVal) && routeVal != null)
                return routeVal.ToString();

            // action args
            if (ctx.ActionArguments.TryGetValue(key, out var argVal) && argVal != null)
                return argVal.ToString();

            // form
            if (ctx.HttpContext.Request.HasFormContentType)
            {
                var form = ctx.HttpContext.Request.Form;
                if (form.ContainsKey(key)) return form[key].ToString();
            }

            // query
            var q = ctx.HttpContext.Request.Query;
            if (q.ContainsKey(key)) return q[key].ToString();

            return null;
        }

        private static bool WasSuccessful(ActionExecutedContext executed)
        {
            if (executed.Exception != null) return false;

            // Convención: JsonResult { success = true/false }
            if (executed.Result is JsonResult jr && jr.Value is not null)
            {
                // Evita reflection pesada: intenta tratarlo como diccionario
                if (jr.Value is System.Collections.IDictionary dict && dict.Contains("success"))
                {
                    var v = dict["success"];
                    if (v is bool b) return b;
                }
                // O como objeto con prop success simple
                var prop = jr.Value.GetType().GetProperty("success");
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    var val = (bool)(prop.GetValue(jr.Value) ?? false);
                    return val;
                }
            }

            // ObjectResult con status 2xx
            if (executed.Result is ObjectResult or)
            {
                var sc = or.StatusCode ?? 200;
                return sc >= 200 && sc < 300;
            }

            // StatusCodeResult 2xx
            if (executed.Result is StatusCodeResult scr)
                return scr.StatusCode >= 200 && scr.StatusCode < 300;

            // ViewResult/PartialViewResult/EmptyResult sin excepción => OK
            return true;
        }
    }
}
