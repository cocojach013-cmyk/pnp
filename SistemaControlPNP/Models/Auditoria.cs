using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaControlPNP.Models
{
    [Table("Auditoria")]
    public class Auditoria
    {
        [Key]
        public long IdAuditoria { get; set; }

        public int? IdUsuario { get; set; }            // opcional si la acción es anónima
        [MaxLength(64)]
        public string Accion { get; set; } = "";

        [MaxLength(128)]
        public string? RegistroID { get; set; }        // id lógico del registro afectado

        [MaxLength(1024)]
        public string? Descripcion { get; set; }       // breve detalle

        [MaxLength(64)]
        public string? IpUsuario { get; set; }

        public DateTime FechaHora { get; set; }        // UTC

        public DateTime? InicioSesion { get; set; }    // cuando aplica (login)

        // nav
        public Usuario? UsuarioRef { get; set; }
    }
}
