using System.Threading.Tasks;
using ControlPNP.Services;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ControlPNP.Filters
{
    public class GlobalAuditFilter : IAsyncActionFilter
    {
        private readonly IAuditService _audit;
        public GlobalAuditFilter(IAuditService audit) { _audit = audit; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var desc = context.ActionDescriptor as ControllerActionDescriptor;
            var isGet = string.Equals(context.HttpContext.Request.Method, "GET", System.StringComparison.OrdinalIgnoreCase);
            var controller = desc?.ControllerName ?? "";
            var action = desc?.ActionName ?? "";

            // Evita auditar el propio listado de auditoría y estáticos
            if (isGet && controller != "Auditoria" && !controller.Equals("StaticFiles"))
            {
                await _audit.LogAsync("NAVIGATE", registroId: $"{controller}/{action}");
            }

            await next();
        }
    }
}
