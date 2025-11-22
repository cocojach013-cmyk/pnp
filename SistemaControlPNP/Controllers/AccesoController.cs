using System.Security.Claims;
using System.Text.RegularExpressions;
using ControlPNP.Data;                   // ControlDBContext
using ControlPNP.Filters;                // AuditAttribute (si lo usas en otros endpoints)
using ControlPNP.Service;                // IPasswordService
using ControlPNP.Services;               // IAuditService
using ControlPNP.ViewModels;             // LoginVM
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;          // Personal, Usuario

namespace ControlPNP.Controllers
{
    public class AccesoController : Controller
    {
        private readonly ControlDBContext _db;
        private readonly IPasswordService _passwords;
        private readonly IWebHostEnvironment _env;
        private readonly IAuditService _audit;

        public AccesoController(
            ControlDBContext db,
            IPasswordService passwords,
            IWebHostEnvironment env,
            IAuditService audit)
        {
            _db = db;
            _passwords = passwords;
            _env = env;
            _audit = audit;
        }

        // =========================================================
        // Helper: guardar foto en /wwwroot/uploads/personal
        // =========================================================
        private async Task<string?> GuardarFotoPersonalAsync(IFormFile foto)
        {
            if (foto == null || foto.Length == 0) return null;

            var permitidos = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(foto.FileName)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext) || !permitidos.Contains(ext))
                throw new InvalidOperationException("Formato no permitido. Use JPG, PNG, GIF o WEBP.");

            const long maxBytes = 2 * 1024 * 1024; // 2MB
            if (foto.Length > maxBytes)
                throw new InvalidOperationException("La imagen supera 2MB.");

            var uploadsRoot = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "personal");
            Directory.CreateDirectory(uploadsRoot);

            var safeName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadsRoot, safeName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                await foto.CopyToAsync(stream);

            return $"/uploads/personal/{safeName}";
        }

        // =========================================================
        // Helper: crear admin con CIP del Personal Id=1 si NO hay usuarios
        // UsuarioLogin = NumeroCip del Personal(1)
        // Contraseña por defecto = Admin*2025
        // =========================================================
        private async Task CrearAdminSiNoExisteAsync()
        {
            // Si ya hay usuarios, no hacer nada
            if (await _db.Usuarios.AnyAsync()) return;

            // Buscar el Personal base (Id=1, sembrado en la migración)
            var persona = await _db.Personal.FirstOrDefaultAsync(p => p.IdPersonal == 1);
            if (persona == null)
                throw new InvalidOperationException("No existe el registro de Personal con Id=1 para crear el administrador.");

            if (string.IsNullOrWhiteSpace(persona.NumeroCip))
                throw new InvalidOperationException("El Personal(Id=1) no tiene Número CIP para usar como usuario.");

            // El usuario será el CIP
            var usuarioLogin = persona.NumeroCip.Trim();

            // Por si existe alguno con ese login (raro, pero se verifica)
            var existe = await _db.Usuarios.AnyAsync(u => u.UsuarioLogin == usuarioLogin);
            if (existe) return;

            var clavePorDefecto = "Admin*2025";

            var usuario = new Usuario
            {
                IdPersonal = persona.IdPersonal,                // = 1
                UsuarioLogin = usuarioLogin,                     // CIP
                ContrasenaHash = _passwords.Hash(clavePorDefecto),
                Rol = "Administrador",
                Estado = true
            };

            await _db.Usuarios.AddAsync(usuario);
            await _db.SaveChangesAsync();

            // Auditoría opcional
            await _audit.LogAsync(
                "USUARIO_AUTOINIT",
                usuario.IdUsuario.ToString(),
                $"Creado admin inicial UsuarioLogin={usuario.UsuarioLogin} (CIP).");
        }

        // =========================================================
        // Endpoint para inicializar admin (lo llama la vista Login con fetch)
        // =========================================================
        [HttpPost, AllowAnonymous, IgnoreAntiforgeryToken]
        public async Task<IActionResult> InitAdmin()
        {
            try
            {
                await CrearAdminSiNoExisteAsync();
                var hayUsuarios = await _db.Usuarios.AnyAsync();
                return Json(new { success = true, any = hayUsuarios });
            }
            catch (Exception ex)
            {
                try
                {
                    await _audit.LogAsync("ERROR_INIT_ADMIN", descripcion: ex.Message);
                }
                catch
                {
                    // noop
                }

                return Json(new
                {
                    success = false,
                    message = "No se pudo inicializar el usuario administrador."
                });
            }
        }

        // =========================================================
        // LOGIN - Página completa
        // =========================================================
        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            // Solo para compatibilidad si tu vista lo usa
            ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();
            ViewBag.ExistenPersonalRegistrado = await _db.Personal.AnyAsync();
            return View();
        }

        [HttpPost, AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVM modelo)
        {
            // Detectar submit vía fetch para responder JSON
            bool isFetch =
                string.Equals(Request.Headers["X-Requested-With"], "fetch", StringComparison.OrdinalIgnoreCase) ||
                (Request.Headers["Accept"].ToString()?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);

            if (!ModelState.IsValid)
            {
                if (isFetch)
                    return Json(new { success = false, message = "Ingrese un usuario y contraseña válidos." });

                ViewData["Mensaje"] = "Ingrese un usuario y contraseña válidos.";
                ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();
                return View();
            }

            try
            {
                var usuario = await _db.Usuarios
                    .Include(u => u.Personal)
                    .FirstOrDefaultAsync(u => u.UsuarioLogin == modelo.Usuario);

                if (usuario == null || !_passwords.Verify(modelo.Clave, usuario.ContrasenaHash))
                {
                    if (isFetch)
                        return Json(new { success = false, message = "Usuario o contraseña incorrectos." });

                    ViewData["Mensaje"] = "Usuario o contraseña incorrectos.";
                    ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();
                    return View();
                }

                if (!usuario.Estado)
                {
                    if (isFetch)
                        return Json(new { success = false, message = "Usuario desactivado, comuníquese con el administrador." });

                    ViewData["Mensaje"] = "Usuario desactivado, comuníquese con el administrador.";
                    ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();
                    return View();
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, $"{usuario.Personal?.Nombres} {usuario.Personal?.Apellidos}".Trim()),
                    new Claim("Grado", usuario.Personal?.Grado ?? string.Empty),
                    new Claim(ClaimTypes.Role, usuario.Rol ?? string.Empty),
                    new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                    new Claim("Usuario", usuario.UsuarioLogin),
                    new Claim("FotoUrl", usuario.Personal?.Foto ?? string.Empty)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var props = new AuthenticationProperties { AllowRefresh = true };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    props);

                await _audit.LogSigninAsync(DateTime.Now, $"Login de {usuario.UsuarioLogin}");

                var redirectUrl = Url.Action("Index", "Dashboard") ?? "/";

                if (isFetch)
                    return Json(new { success = true, redirect = redirectUrl });

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                try
                {
                    await _audit.LogAsync("ERROR_LOGIN", descripcion: ex.Message);
                }
                catch
                {
                    // noop
                }

                if (isFetch)
                    return Json(new { success = false, message = "No se pudo procesar el inicio de sesión." });

                ViewData["Mensaje"] = "No se pudo procesar el inicio de sesión.";
                return View();
            }
        }

        // =========================================================
        // LOGIN - Modal (si lo usas)
        // =========================================================
        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> LoginModal()
        {
            ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();
            return PartialView("_LoginModal", new LoginVM());
        }

        [HttpPost, AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginModal(LoginVM modelo)
        {
            ViewBag.ExistenUsuariosRegistrados = await _db.Usuarios.AnyAsync();

            if (!ModelState.IsValid)
            {
                ViewData["Mensaje"] = "Ingrese un usuario y contraseña válidos.";
                return PartialView("_LoginModal", modelo);
            }

            try
            {
                var usuario = await _db.Usuarios
                    .Include(u => u.Personal)
                    .FirstOrDefaultAsync(u => u.UsuarioLogin == modelo.Usuario);

                if (usuario == null || !_passwords.Verify(modelo.Clave, usuario.ContrasenaHash))
                {
                    ViewData["Mensaje"] = "Usuario o contraseña incorrectos.";
                    return PartialView("_LoginModal", modelo);
                }

                if (!usuario.Estado)
                {
                    ViewData["Mensaje"] = "Usuario desactivado, comuníquese con el administrador.";
                    return PartialView("_LoginModal", modelo);
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, $"{usuario.Personal?.Nombres} {usuario.Personal?.Apellidos}".Trim()),
                    new Claim("Grado", usuario.Personal?.Grado ?? string.Empty),
                    new Claim(ClaimTypes.Role, usuario.Rol ?? string.Empty),
                    new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                    new Claim("Usuario", usuario.UsuarioLogin),
                    new Claim("FotoUrl", usuario.Personal?.Foto ?? string.Empty)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var props = new AuthenticationProperties { AllowRefresh = true };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    props);

                await _audit.LogSigninAsync(DateTime.Now, $"Login de {usuario.UsuarioLogin}");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                try
                {
                    await _audit.LogAsync("ERROR_LOGIN_MODAL", descripcion: ex.Message);
                }
                catch
                {
                    // noop
                }

                ViewData["Mensaje"] = "No se pudo procesar el inicio de sesión.";
                return PartialView("_LoginModal", modelo);
            }
        }

        // =========================================================
        // CAMBIAR CONTRASEÑA (modal del layout)
        // =========================================================
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarClave(string oldPassword, string newPassword, string? confirmPassword)
        {
            try
            {
                // Validaciones básicas
                if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
                {
                    return Json(new { success = false, message = "Completa todos los campos." });
                }

                if (!Regex.IsMatch(newPassword, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!%*?&]).{6,}$"))
                {
                    return Json(new
                    {
                        success = false,
                        message = "La contraseña no cumple los requisitos."
                    });
                }

                if (!string.IsNullOrWhiteSpace(confirmPassword) && newPassword != confirmPassword)
                {
                    return Json(new
                    {
                        success = false,
                        message = "La nueva contraseña y la confirmación no coinciden."
                    });
                }

                if (oldPassword == newPassword)
                {
                    return Json(new
                    {
                        success = false,
                        message = "La nueva contraseña no puede ser igual a la actual."
                    });
                }

                // Usuario actual
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var idUsuario))
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo identificar al usuario actual."
                    });
                }

                var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);
                if (usuario == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Usuario no encontrado."
                    });
                }

                // Validar contraseña actual
                if (!_passwords.Verify(oldPassword, usuario.ContrasenaHash))
                {
                    return Json(new
                    {
                        success = false,
                        message = "La contraseña actual no es correcta."
                    });
                }

                // Actualizar contraseña
                usuario.ContrasenaHash = _passwords.Hash(newPassword);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "USUARIO_CAMBIAR_CLAVE",
                    idUsuario.ToString(),
                    "Contraseña actualizada correctamente."
                );

                return Json(new
                {
                    success = true,
                    message = "Tu contraseña se actualizó correctamente."
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await _audit.LogAsync("ERROR_CAMBIAR_CLAVE", descripcion: ex.Message);
                }
                catch
                {
                    // noop
                }

                return Json(new
                {
                    success = false,
                    message = "Ocurrió un error en el servidor al cambiar la contraseña."
                });
            }
        }

        // =========================================================
        // ACTUALIZAR FOTO PERFIL
        // =========================================================
        [HttpPost, Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarFotoPerfil(IFormFile avatar)
        {
            if (avatar == null || avatar.Length == 0)
                return Json(new { success = false, message = "Selecciona una imagen válida." });

            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var idUsuario))
                    return Json(new { success = false, message = "No se pudo identificar al usuario." });

                var usuario = await _db.Usuarios
                    .Include(u => u.Personal)
                    .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

                if (usuario == null)
                    return Json(new { success = false, message = "Usuario no encontrado." });

                string? nuevaUrl;
                try
                {
                    nuevaUrl = await GuardarFotoPersonalAsync(avatar);
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al subir la foto: " + ex.Message });
                }

                if (!string.IsNullOrWhiteSpace(nuevaUrl) && usuario.Personal != null)
                    usuario.Personal.Foto = nuevaUrl;

                await _db.SaveChangesAsync();

                // refrescar claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, $"{usuario.Personal?.Nombres} {usuario.Personal?.Apellidos}".Trim()),
                    new Claim("Grado", usuario.Personal?.Grado ?? string.Empty),
                    new Claim(ClaimTypes.Role, usuario.Rol ?? string.Empty),
                    new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                    new Claim("Usuario", usuario.UsuarioLogin),
                    new Claim("FotoUrl", usuario.Personal?.Foto ?? string.Empty)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));

                await _audit.LogAsync("USUARIO_ACTUALIZAR_FOTO", idUsuario.ToString(), $"NuevaFoto={nuevaUrl}");

                return Json(new { success = true, message = "Foto de perfil actualizada.", url = nuevaUrl });
            }
            catch (Exception ex)
            {
                try
                {
                    await _audit.LogAsync("ERROR_FOTO_PERFIL", descripcion: ex.Message);
                }
                catch
                {
                    // noop
                }
                return Json(new { success = false, message = "No se pudo actualizar la foto de perfil." });
            }
        }

        // =========================================================
        // LOGOUT
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> CerrarSesion()
        {
            await _audit.LogAsync("LOGOUT", descripcion: "Cierre de sesión (POST)");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await _audit.LogAsync("LOGOUT", descripcion: "Cierre de sesión (GET)");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // =========================================================
        // 403
        // =========================================================
        [HttpGet, AllowAnonymous]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            Response.StatusCode = 403;
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
    }
}
