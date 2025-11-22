namespace ControlPNP.ViewModels
{
    public class AuditoriaViewModel
    {
        public long IdAuditoria { get; set; }

        // HAZLO NULLABLE para que coincida con la entidad (int?)
        public int? IdUsuario { get; set; }

        public string? UsuarioLogin { get; set; }

        public string Accion { get; set; } = string.Empty;
        public string? RegistroID { get; set; }
        public string? Descripcion { get; set; }
        public string? IpUsuario { get; set; }

        public DateTime FechaHoraLocal { get; set; }
        public DateTime? InicioSesionLocal { get; set; }
    }
}
