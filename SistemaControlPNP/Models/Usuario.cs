using System;

namespace SistemaControlPNP.Models
{
    public class Usuario
    {
        public int IdUsuario { get; set; }
        public int IdPersonal { get; set; }             // FK a Personal
        public string UsuarioLogin { get; set; }        // nombre de usuario
        public string ContrasenaHash { get; set; }      // contraseña en formato hash
        public string Rol { get; set; }                 // Admin / Operador / Consulta
        public bool Estado { get; set; }                // activo o inactivo

        // Relación con Personal (opcional, para EF Core)
        public Personal Personal { get; set; }
    }
}
