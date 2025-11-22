namespace ControlPNP.ViewModels
{
    public class UsuarioVM
    {
        public int IdUsuario { get; set; }
        public int IdPersonal { get; set; }

        // Campo de formulario (se mapeará a Usuario.UsuarioLogin)
        public string Usuario { get; set; }

        // Clave en texto para entrada (se hashea)
        public string Clave { get; set; }
        public string ConfirmarClave { get; set; }

        // Rol visible en UI (se mapeará a Usuario.Rol)
        public string Rol { get; set; }

        public bool Estado { get; set; }

        // Clave admin (oculta) para autorizar
        public string AdminPassword { get; set; }
    }
}
