using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ControlPNP.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;
using System.Collections.Generic;
using ControlPNP.Filters;

namespace ControlPNP.Controllers
{
    // [Authorize(Roles = "Administrador")]
    public class PersonalController : Controller
    {
        private readonly ControlDBContext _db;
        private readonly IWebHostEnvironment _env;

        public PersonalController(ControlDBContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // ====== RANGOS / GRADOS ======
        private static readonly string[] Rangos = new[]
        {
            "Gral. PNP","Tnte. Gral. PNP","Crnl. PNP","Cmdte. PNP","May. PNP","Cap. PNP","Tnte. PNP","Alfz. PNP",
            "Gral. S PNP","Crnl. S PNP","Cmdte. S PNP","May. S PNP","Cap. S PNP","Tnte. S PNP",
            "SS. PNP","SB. PNP","ST1. PNP","ST2. PNP","ST3. PNP","S1. PNP","S2. PNP","S3. PNP",
            "SS S PNP","SB S PNP","ST1 S PNP","ST2 S PNP","ST3 S PNP","S1 S PNP","S2 S PNP","S3 S PNP"
        };

        private static List<SelectListItem> GradosSelect(string? selected = null)
            => Rangos
                .Select(r => new SelectListItem { Text = r, Value = r, Selected = (r == selected) })
                .Prepend(new SelectListItem { Text = "Seleccionar", Value = "" })
                .ToList();

        // ====== Normalizadores ======
        private static string? UpperOrNull(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

        private static string DigitsOnly(string? s)
            => new string((s ?? "").Where(char.IsDigit).ToArray());

        private const string EmailDomain = "@policia.gob.pe";
        private static readonly Regex EmailRegex = new(
            @"^[A-Za-z0-9._%+-]+@policia\.gob\.pe$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string NormalizeEmailToDomainOrEmpty(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;
            var v = email.Trim().ToLowerInvariant();

            if (!v.Contains('@') || v.EndsWith("@"))
            {
                v = v.Replace("@", "");
                v = v + EmailDomain;
            }
            else
            {
                var parts = v.Split('@');
                var local = parts[0];
                var dom = parts.Length > 1 ? parts[1] : "";
                if (!dom.Equals(EmailDomain.TrimStart('@'), StringComparison.OrdinalIgnoreCase))
                    v = local + EmailDomain;
            }

            return EmailRegex.IsMatch(v) ? v : string.Empty;
        }

        private static void NormalizePersonalForSave(Personal m)
        {
            m.Grado = UpperOrNull(m.Grado);
            m.Nombres = UpperOrNull(m.Nombres) ?? "";
            m.Apellidos = UpperOrNull(m.Apellidos) ?? "";
            m.NumeroCip = DigitsOnly(m.NumeroCip);
            m.Dni = DigitsOnly(m.Dni);
            m.Telefono = UpperOrNull(m.Telefono);
            m.Direccion = UpperOrNull(m.Direccion);
            m.Unidad = UpperOrNull(m.Unidad);
            m.Sexo = (m.Sexo ?? "").Trim().ToUpperInvariant();
            m.FechaNacimiento = m.FechaNacimiento.Date;
            m.CorreoInstitucional = NormalizeEmailToDomainOrEmpty(m.CorreoInstitucional);
        }

        private static bool SexoValido(string? sexo)
        {
            var s = (sexo ?? "").Trim().ToUpperInvariant();
            return s == "M" || s == "F";
        }

        private static bool FechaNacimientoValida(DateTime fecha)
        {
            var d = fecha.Date;
            return d >= new DateTime(1900, 1, 1) && d <= DateTime.UtcNow.Date;
        }

        private async Task<bool> ValidarServidorAsync(Personal m, bool esEdicion)
        {
            if (!SexoValido(m.Sexo))
                ModelState.AddModelError(nameof(m.Sexo), "Seleccione un sexo válido (Masculino o Femenino).");

            if (!FechaNacimientoValida(m.FechaNacimiento))
                ModelState.AddModelError(nameof(m.FechaNacimiento), "La fecha de nacimiento es inválida (rango: 1900-01-01 a hoy).");

            if (string.IsNullOrWhiteSpace(m.Dni) || m.Dni.Length != 8)
                ModelState.AddModelError(nameof(m.Dni), "El DNI debe contener exactamente 8 dígitos.");

            if (string.IsNullOrWhiteSpace(m.NumeroCip) || m.NumeroCip.Length != 8)
                ModelState.AddModelError(nameof(m.NumeroCip), "El CIP debe contener exactamente 8 dígitos.");

            if (string.IsNullOrWhiteSpace(m.CorreoInstitucional) || !EmailRegex.IsMatch(m.CorreoInstitucional))
                ModelState.AddModelError(nameof(m.CorreoInstitucional), "El correo institucional debe ser del dominio @policia.gob.pe.");

            if (esEdicion)
            {
                if (await _db.Personal.AnyAsync(x => x.Dni == m.Dni && x.IdPersonal != m.IdPersonal))
                    ModelState.AddModelError(nameof(m.Dni), "Ya existe un personal con este DNI.");
                if (await _db.Personal.AnyAsync(x => x.NumeroCip == m.NumeroCip && x.IdPersonal != m.IdPersonal))
                    ModelState.AddModelError(nameof(m.NumeroCip), "Ya existe un personal con este CIP.");
            }
            else
            {
                if (await _db.Personal.AnyAsync(x => x.Dni == m.Dni))
                    ModelState.AddModelError(nameof(m.Dni), "Ya existe un personal con este DNI.");
                if (await _db.Personal.AnyAsync(x => x.NumeroCip == m.NumeroCip))
                    ModelState.AddModelError(nameof(m.NumeroCip), "Ya existe un personal con este CIP.");
            }

            return ModelState.IsValid;
        }

        private static string PrimerError(ModelStateDictionary modelState)
        {
            var entry = modelState.FirstOrDefault(kv => kv.Value.Errors.Count > 0);
            return entry.Value?.Errors.FirstOrDefault()?.ErrorMessage ?? "Hay errores en el formulario.";
        }

        // ====== LISTA ======
        [HttpGet]
        public async Task<IActionResult> ListaPersonal()
        {
            try
            {
                var list = await _db.Personal
                    .AsNoTracking()
                    .OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .ToListAsync();

                return View(list);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al listar personal: {ex.Message}" });
            }
        }

        // ====== REGISTRAR (GET parcial) ======
        [HttpGet]
        public IActionResult RegistrarPersonal()
        {
            try
            {
                ViewBag.GradosList = GradosSelect();
                return PartialView("RegistrarPersonal", new Personal { Estado = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al preparar el formulario: {ex.Message}" });
            }
        }

        // ====== REGISTRAR (POST) ======
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("PERSONAL_CREAR", registroKey: "NumeroCip")]
        public async Task<IActionResult> RegistrarPersonal(Personal m, IFormFile? fotoFile, string? HuellaBase64)
        {
            try
            {
                NormalizePersonalForSave(m);

                ModelState.Clear();
                TryValidateModel(m);

                var ok = await ValidarServidorAsync(m, esEdicion: false);
                if (!ok)
                    return Json(new { success = false, message = PrimerError(ModelState) });

                // Foto (opcional)
                if (fotoFile is { Length: > 0 })
                {
                    try { m.Foto = await GuardarFotoAsync(fotoFile); }
                    catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
                }

                // Huella (opcional)
                if (!string.IsNullOrWhiteSpace(HuellaBase64))
                {
                    try
                    {
                        var b64 = HuellaBase64;
                        var comma = b64.IndexOf(',');
                        if (comma >= 0) b64 = b64[(comma + 1)..];
                        m.Huella = Convert.FromBase64String(b64);
                    }
                    catch { return Json(new { success = false, message = "La huella capturada es inválida." }); }
                }

                if (m.FechaRegistro == default) m.FechaRegistro = DateTime.UtcNow;

                try
                {
                    _db.Personal.Add(m);
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, message = "Personal registrado correctamente." });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Json(new { success = false, message = "Concurrencia detectada. Vuelve a intentar." });
                }
                catch
                {
                    return Json(new { success = false, message = "No se pudo guardar el registro." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al registrar personal: {ex.Message}" });
            }
        }

        // ====== EDITAR (GET) ======
        [HttpGet]
        public async Task<IActionResult> EditarPersonal(int id)
        {
            try
            {
                var p = await _db.Personal.FindAsync(id);
                if (p == null) return NotFound("No se encontró el registro.");

                ViewBag.GradosList = GradosSelect(p.Grado);
                return PartialView("EditarPersonal", p);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al cargar la edición: {ex.Message}" });
            }
        }

        // ====== EDITAR (POST) ======
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("PERSONAL_EDITAR", registroKey: "IdPersonal")]
        public async Task<IActionResult> EditarPersonal(Personal m, IFormFile? fotoFile, string? HuellaBase64)
        {
            try
            {
                NormalizePersonalForSave(m);

                ModelState.Clear();
                TryValidateModel(m);

                var p = await _db.Personal.FindAsync(m.IdPersonal);
                if (p == null) return Json(new { success = false, message = "El personal no fue encontrado." });

                var ok = await ValidarServidorAsync(m, esEdicion: true);
                if (!ok)
                    return Json(new { success = false, message = PrimerError(ModelState) });

                bool huboCambio = false;

                bool SetIfDifferent<T>(T actual, T nuevo, Action<T> setter, IEqualityComparer<T>? cmp = null)
                {
                    cmp ??= EqualityComparer<T>.Default;
                    if (!cmp.Equals(actual, nuevo))
                    {
                        setter(nuevo);
                        return true;
                    }
                    return false;
                }

                huboCambio |= SetIfDifferent(p.Grado, m.Grado, v => p.Grado = v);
                huboCambio |= SetIfDifferent(p.Nombres, m.Nombres, v => p.Nombres = v);
                huboCambio |= SetIfDifferent(p.Apellidos, m.Apellidos, v => p.Apellidos = v);
                huboCambio |= SetIfDifferent(p.NumeroCip, m.NumeroCip, v => p.NumeroCip = v);
                huboCambio |= SetIfDifferent(p.Dni, m.Dni, v => p.Dni = v);
                huboCambio |= SetIfDifferent(p.Telefono, m.Telefono, v => p.Telefono = v);
                huboCambio |= SetIfDifferent(p.Direccion, m.Direccion, v => p.Direccion = v);
                huboCambio |= SetIfDifferent(p.Unidad, m.Unidad, v => p.Unidad = v);
                huboCambio |= SetIfDifferent(p.Sexo, m.Sexo, v => p.Sexo = v);
                huboCambio |= SetIfDifferent(p.FechaNacimiento, m.FechaNacimiento, v => p.FechaNacimiento = v);
                huboCambio |= SetIfDifferent(p.CorreoInstitucional, m.CorreoInstitucional, v => p.CorreoInstitucional = v);
                huboCambio |= SetIfDifferent(p.Estado, m.Estado, v => p.Estado = v);

                // Foto nueva (opcional)
                if (fotoFile is { Length: > 0 })
                {
                    try
                    {
                        var nuevaRuta = await GuardarFotoAsync(fotoFile);
                        if (!string.Equals(p.Foto, nuevaRuta, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Foto = nuevaRuta;
                            huboCambio = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                // Huella nueva (opcional)
                if (!string.IsNullOrWhiteSpace(HuellaBase64))
                {
                    try
                    {
                        var b64 = HuellaBase64;
                        var comma = b64.IndexOf(',');
                        if (comma >= 0) b64 = b64[(comma + 1)..];
                        var nuevosBytes = Convert.FromBase64String(b64);

                        bool iguales = p.Huella != null && nuevosBytes != null
                                       && p.Huella.Length == nuevosBytes.Length
                                       && p.Huella.SequenceEqual(nuevosBytes);

                        if (!iguales)
                        {
                            p.Huella = nuevosBytes;
                            huboCambio = true;
                        }
                    }
                    catch
                    {
                        return Json(new { success = false, message = "La huella capturada es inválida." });
                    }
                }

                if (!huboCambio)
                    return Json(new { success = false, message = "No se realizaron cambios." });

                try
                {
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, message = "Personal actualizado correctamente." });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Json(new { success = false, message = "El registro fue modificado por otro usuario. Recarga e inténtalo de nuevo." });
                }
                catch
                {
                    return Json(new { success = false, message = "No se pudo actualizar el registro." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al editar personal: {ex.Message}" });
            }
        }

        // ====== DESACTIVAR ======
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("PERSONAL_DESACTIVAR", registroKey: "id")]
        public async Task<IActionResult> DesactivarPersonal(int id)
        {
            try
            {
                var p = await _db.Personal.FindAsync(id);
                if (p == null) return Json(new { success = false, message = "No se encontró el personal." });

                if (!p.Estado)
                    return Json(new { success = true, message = "El personal ya estaba inactivo." });

                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    p.Estado = false;

                    var usuarios = await _db.Usuarios.Where(u => u.IdPersonal == id && u.Estado).ToListAsync();
                    foreach (var u in usuarios) u.Estado = false;

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = usuarios.Count > 0
                            ? $"Personal marcado como inactivo. También se desactivaron {usuarios.Count} usuario(s) asociado(s)."
                            : "Personal marcado como inactivo."
                    });
                }
                catch
                {
                    await tx.RollbackAsync();
                    return Json(new { success = false, message = "No se pudo desactivar el personal." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al desactivar personal: {ex.Message}" });
            }
        }

        // ====== ACTIVAR ======
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Audit("PERSONAL_ACTIVAR", registroKey: "id")]
        public async Task<IActionResult> ActivarPersonal(int id)
        {
            try
            {
                var p = await _db.Personal.FindAsync(id);
                if (p == null) return Json(new { success = false, message = "No se encontró el personal." });

                if (p.Estado)
                    return Json(new { success = true, message = "El personal ya estaba activo." });

                try
                {
                    p.Estado = true;
                    await _db.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        message = "Personal activado correctamente. Los usuarios asociados permanecen sin cambios."
                    });
                }
                catch
                {
                    return Json(new { success = false, message = "No se pudo activar el personal." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al activar personal: {ex.Message}" });
            }
        }

        // ====== Guardar fotos ======
        private async Task<string> GuardarFotoAsync(IFormFile foto)
        {
            var permitidos = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(foto.FileName)?.ToLowerInvariant();
            var contentOk = foto.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(ext) || !permitidos.Contains(ext) || !contentOk)
                throw new InvalidOperationException("Formato de foto no permitido. Use JPG, PNG o WEBP.");

            if (foto.Length > 2 * 1024 * 1024)
                throw new InvalidOperationException("La imagen supera 2MB.");

            var uploadsRoot = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "personal");
            Directory.CreateDirectory(uploadsRoot);

            var safe = $"{Guid.NewGuid():N}{ext}";
            var full = Path.Combine(uploadsRoot, safe);
            await using (var fs = new FileStream(full, FileMode.Create))
            {
                await foto.CopyToAsync(fs);
            }

            return $"/uploads/personal/{safe}";
        }
    }
}
