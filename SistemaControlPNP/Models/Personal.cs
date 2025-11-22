using System;

namespace SistemaControlPNP.Models
{
    public class Personal
    {
        public int IdPersonal { get; set; }

        // Opcional
        public string? Grado { get; set; }

        // Requeridos (según configuración fluida)
        public string Nombres { get; set; } = null!;
        public string Apellidos { get; set; } = null!;
        public string NumeroCip { get; set; } = null!;  // 8 dígitos
        public string Dni { get; set; } = null!;        // 8 dígitos

        // Opcionales
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public string? CorreoInstitucional { get; set; }
        public string? Unidad { get; set; }
        public string? Foto { get; set; }
        public byte[]? Huella { get; set; }

           public string Sexo { get; set; } = null!;
        public DateTime FechaNacimiento { get; set; }

        public DateTime FechaRegistro { get; set; }
        public bool Estado { get; set; }
    }
}
