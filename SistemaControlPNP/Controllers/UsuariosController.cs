using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ControlPNP.Data;
using ControlPNP.Service;
using ControlPNP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;
using ControlPNP.Filters;

namespace SistemaControlPNP.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class UsuariosController : Controller
    {
        private readonly ControlDBContext _DB;
        private readonly IPasswordService _pwd;

        public UsuariosController(ControlDBContext db, IPasswordService pwd)
        {
            _DB = db;
            _pwd = pwd;
        }

        // ============ Helpers ============

        private async Task<(bool ok, string msg, Usuario? admin)> ValidateAdminAsync(string adminPassword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(adminPassword))
                    return (false, "Ingrese su clave de administrador.", null);

                var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(idStr, out var id))
                    return (false, "Sesión no válida.", null);

                var admin = await _DB.Usuarios.FindAsync(id);
                if (admin == null) return (false, "Usuario autenticado no encontrado.", null);

                if (!string.Equals(admin.Rol, "Administrador", StringComparison.OrdinalIgnoreCase))
                    return (false, "No tiene permisos de administrador.", null);

                if (!_pwd.Verify(adminPassword, admin.ContrasenaHash))
                    return (false, "Clave de administrador incorrecta.", null);

                return (true, "", admin);
            }
            catch (Exception ex)
            {
                return (false, $"Error al validar administrador: {ex.Message}", null);
            }
        }

        private int CurrentAdminId()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idStr, out var id) ? id : 0;
        }

        private static bool Strong(string p) =>
            !string.IsNullOrWhiteSpace(p) &&
            Regex.IsMatch(p, "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^A-Za-z0-9]).{6,}$");

        // ============ Lista ============

        [HttpGet]
        public async Task<IActionResult> ListaUsuarios()
        {
            try
            {
                var usuarios = await _DB.Usuarios.AsNoTracking().ToListAsync();

                var personalData = await _DB.Personal
                    .AsNoTracking()
                    .ToDictionaryAsync(p => p.IdPersonal, p => new { p.Grado, p.Nombres, p.Apellidos, p.NumeroCip });

                ViewBag.PersonalData = personalData;
                ViewBag.AdminId = CurrentAdminId();

                return View(usuarios);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al listar usuarios: {ex.Message}");
            }
        }

        // ============ Registrar (GET) ============

        [HttpGet]
        public async Task<IActionResult> RegistrarUsuario()
        {
            try
            {
                // Lista básica por si necesitas Html.DropDownListFor en otro lugar
                ViewBag.PersonalList = await _DB.Personal
                    .AsNoTracking()
                    .Where(p => p.Estado)
                    .OrderBy(p => p.Grado).ThenBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Select(p => new SelectListItem
                    {
                        Value = p.IdPersonal.ToString(),
                        Text = p.Grado + " " + p.Nombres + " " + p.Apellidos
                    })
                    .ToListAsync();

                // Lista extendida con CIP para renderizar <option data-cip="...">
                ViewBag.PersonalFull = await _DB.Personal
                    .AsNoTracking()
                    .Where(p => p.Estado)
                    .OrderBy(p => p.Grado).ThenBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Select(p => new
                    {
                        p.IdPersonal,
                        Text = p.Grado + " " + p.Nombres + " " + p.Apellidos,
                        Cip = p.NumeroCip
                    })
                    .ToListAsync();

                return PartialView("RegistrarUsuario");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al preparar el formulario: {ex.Message}" });
            }
        }

        // ============ Editar (GET) ============

        [HttpGet]
        public async Task<IActionResult> EditarUsuario(int id)
        {
            try
            {
                var u = await _DB.Usuarios
                    .Include(x => x.Personal)
                    .FirstOrDefaultAsync(x => x.IdUsuario == id);

                if (u == null) return Json(new { success = false, message = "Usuario no encontrado." });

                var activos = await _DB.Personal
                    .AsNoTracking()
                    .Where(p => p.Estado)
                    .OrderBy(p => p.Grado).ThenBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Select(p => new SelectListItem
                    {
                        Value = p.IdPersonal.ToString(),
                        Text = p.Grado + " " + p.Nombres + " " + p.Apellidos
                    })
                    .ToListAsync();

                // Si el personal asignado está inactivo, lo agregamos para poder mostrarlo en el select
                var asignado = await _DB.Personal.AsNoTracking()
                    .Where(p => p.IdPersonal == u.IdPersonal)
                    .Select(p => new { p.IdPersonal, p.Grado, p.Nombres, p.Apellidos, p.Estado, p.NumeroCip })
                    .FirstOrDefaultAsync();

                if (asignado != null && !activos.Any(x => x.Value == asignado.IdPersonal.ToString()))
                {
                    activos.Add(new SelectListItem
                    {
                        Value = asignado.IdPersonal.ToString(),
                        Text = $"{asignado.Grado} {asignado.Nombres} {asignado.Apellidos} (INACTIVO)"
                    });
                }

                ViewBag.PersonalList = activos;

                // Lista extendida con CIP para data-cip
                ViewBag.PersonalFull = await _DB.Personal
                    .AsNoTracking()
                    .OrderBy(p => p.Grado).ThenBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Select(p => new
                    {
                        p.IdPersonal,
                        Text = p.Grado + " " + p.Nombres + " " + p.Apellidos + (p.Estado ? "" : " (INACTIVO)"),
                        Cip = p.NumeroCip
                    })
                    .ToListAsync();

                ViewBag.PermisoList = new[]
                {
                    new SelectListItem { Value = "Administrador", Text = "Administrador" },
                    new SelectListItem { Value = "Operador",      Text = "Operador" },
                    new SelectListItem { Value = "Invitado",      Text = "Invitado" }
                }.ToList();

                var vm = new UsuarioVM
                {
                    IdUsuario = u.IdUsuario,
                    IdPersonal = u.IdPersonal,
                    // Mostramos el CIP actual como "Usuario" para el display readonly:
                    Usuario = u.Personal?.NumeroCip ?? u.UsuarioLogin ?? "",
                    Rol = u.Rol,
                    Estado = u.Estado
                };

                return PartialView("EditarUsuario", vm);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al cargar la edición: {ex.Message}" });
            }
        }

        // ============ Gate admin (pre-check con clave) ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrecheckAdmin(string adminPassword)
        {
            try
            {
                var (ok, msg, _) = await ValidateAdminAsync(adminPassword);
                return Json(new { success = ok, message = msg });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error en la validación: {ex.Message}" });
            }
        }

        // ============ Registrar (POST) - UsuarioLogin = CIP ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_CREAR", registroKey: "IdPersonal")]
        public async Task<IActionResult> RegistrarUsuario(UsuarioVM m)
        {
            try
            {
                // 🔹 Ya NO se valida aquí la clave de admin.
                // Se validó en PrecheckAdmin antes de abrir el modal.

                if (m.IdPersonal <= 0)
                    return Json(new { success = false, message = "Seleccione el personal." });

                if (string.IsNullOrWhiteSpace(m.Rol))
                    return Json(new { success = false, message = "Seleccione el rol." });

                if (!Strong(m.Clave))
                    return Json(new { success = false, message = "La contraseña no cumple los requisitos." });

                if (m.Clave != m.ConfirmarClave)
                    return Json(new { success = false, message = "Las contraseñas no coinciden." });

                var personal = await _DB.Personal.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdPersonal == m.IdPersonal);
                if (personal == null)
                    return Json(new { success = false, message = "Personal no encontrado." });

                var cip = (personal.NumeroCip ?? "").Trim();
                if (cip.Length == 0)
                    return Json(new { success = false, message = "El personal no tiene CIP registrado." });

                var dup = await _DB.Usuarios.AnyAsync(u => u.UsuarioLogin == cip);
                if (dup)
                    return Json(new { success = false, message = "Ya existe un usuario para ese CIP." });

                var u = new Usuario
                {
                    IdPersonal = m.IdPersonal,
                    UsuarioLogin = cip,                    // Usuario = CIP
                    ContrasenaHash = _pwd.Hash(m.Clave),
                    Rol = m.Rol,
                    Estado = m.Estado
                };

                try
                {
                    _DB.Usuarios.Add(u);
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, message = "Usuario registrado correctamente." });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo registrar el usuario.",
                        detail = ex.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error al registrar usuario: {ex.Message}"
                });
            }
        }

        // ============ Editar (POST) - UsuarioLogin = CIP ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_EDITAR", registroKey: "IdUsuario")]
        public async Task<IActionResult> EditarUsuario(UsuarioVM m)
        {
            try
            {
                // 🔹 Ya NO se valida aquí la clave de admin.
                // Se validó en PrecheckAdmin antes de abrir el modal.

                var u = await _DB.Usuarios.FindAsync(m.IdUsuario);
                if (u == null)
                    return Json(new { success = false, message = "Usuario no encontrado." });

                var personal = await _DB.Personal.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdPersonal == m.IdPersonal);
                if (personal == null)
                    return Json(new { success = false, message = "Personal no encontrado." });

                var cip = (personal.NumeroCip ?? "").Trim();
                if (cip.Length == 0)
                    return Json(new { success = false, message = "El personal no tiene CIP registrado." });

                var dup = await _DB.Usuarios
                    .AnyAsync(x => x.UsuarioLogin == cip && x.IdUsuario != m.IdUsuario);
                if (dup)
                    return Json(new { success = false, message = "Ya existe un usuario para ese CIP." });

                bool hasChanges =
                    u.IdPersonal != m.IdPersonal ||
                    u.Rol != m.Rol ||
                    u.Estado != m.Estado ||
                    u.UsuarioLogin != cip;

                if (!hasChanges)
                    return Json(new { success = false, message = "No se han realizado cambios." });

                u.IdPersonal = m.IdPersonal;
                u.UsuarioLogin = cip;     // forzamos el login = CIP
                u.Rol = m.Rol;
                u.Estado = m.Estado;

                try
                {
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, message = "Usuario actualizado correctamente." });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se pudo actualizar el usuario.",
                        detail = ex.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error al editar usuario: {ex.Message}"
                });
            }
        }

        // ============ Reset clave ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_RESET_CLAVE", registroKey: "id")]
        public async Task<IActionResult> ResetClave(int id, string adminPassword)
        {
            try
            {
                var (ok, msg, _) = await ValidateAdminAsync(adminPassword);
                if (!ok) return Json(new { success = false, message = msg });

                var usuario = await _DB.Usuarios.FindAsync(id);
                if (usuario == null) return Json(new { success = false, message = "Usuario no encontrado." });

                string GenTemp()
                {
                    const string lowers = "abcdefghijkmnopqrstuvwxyz";
                    const string uppers = "ABCDEFGHJKLMNPQRSTUVWXYZ";
                    const string digits = "23456789";
                    const string symbols = "!%*?&+";
                    var rnd = new Random();
                    char Pick(string s) => s[rnd.Next(s.Length)];
                    var chars = new char[10];
                    chars[0] = Pick(lowers); chars[1] = Pick(uppers); chars[2] = Pick(digits); chars[3] = Pick(symbols);
                    var pool = lowers + uppers + digits + symbols;
                    for (int i = 4; i < chars.Length; i++) chars[i] = Pick(pool);
                    for (int i = 0; i < chars.Length; i++) { int j = rnd.Next(i, chars.Length); (chars[i], chars[j]) = (chars[j], chars[i]); }
                    return new string(chars);
                }

                var temp = GenTemp();
                usuario.ContrasenaHash = _pwd.Hash(temp);

                try
                {
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, tempPassword = temp });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo actualizar la contraseña.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al resetear la contraseña: {ex.Message}" });
            }
        }

        // ============ (Des)activar ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_DESACTIVAR", registroKey: "id")]
        public async Task<IActionResult> Desactivar(int id, string adminPassword)
        {
            try
            {
                var (ok, msg, admin) = await ValidateAdminAsync(adminPassword);
                if (!ok) return Json(new { success = false, message = msg });

                if (admin != null && admin.IdUsuario == id)
                    return Json(new { success = false, message = "No puede desactivar su propio usuario." });

                var u = await _DB.Usuarios.FindAsync(id);
                if (u == null) return Json(new { success = false, message = "Usuario no encontrado." });

                if (!u.Estado)
                    return Json(new { success = false, message = "El usuario ya está inactivo." });

                u.Estado = false;

                try
                {
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, message = "Usuario desactivado correctamente." });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo desactivar el usuario.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al desactivar usuario: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_ACTIVAR", registroKey: "id")]
        public async Task<IActionResult> Activar(int id, string adminPassword)
        {
            try
            {
                var (ok, msg, _) = await ValidateAdminAsync(adminPassword);
                if (!ok) return Json(new { success = false, message = msg });

                var u = await _DB.Usuarios.FindAsync(id);
                if (u == null) return Json(new { success = false, message = "Usuario no encontrado." });

                if (u.Estado)
                    return Json(new { success = false, message = "El usuario ya está activo." });

                u.Estado = true;

                try
                {
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, message = "Usuario activado correctamente." });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo activar el usuario.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al activar usuario: {ex.Message}" });
            }
        }

        // ============ Eliminar ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("USUARIO_ELIMINAR", registroKey: "id")]
        public async Task<IActionResult> Eliminar(int id, string adminPassword)
        {
            try
            {
                var (ok, msg, admin) = await ValidateAdminAsync(adminPassword);
                if (!ok) return Json(new { success = false, message = msg });

                if (admin != null && admin.IdUsuario == id)
                    return Json(new { success = false, message = "No puede eliminar su propio usuario." });

                var u = await _DB.Usuarios.FindAsync(id);
                if (u == null) return Json(new { success = false, message = "Usuario no encontrado." });

                _DB.Usuarios.Remove(u);

                try
                {
                    await _DB.SaveChangesAsync();
                    return Json(new { success = true, message = "Usuario eliminado exitosamente." });
                }
                catch (DbUpdateException ex)
                {
                    return Json(new { success = false, message = "No se pudo eliminar el usuario.", detail = ex.Message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al eliminar usuario: {ex.Message}" });
            }
        }
    }
}
