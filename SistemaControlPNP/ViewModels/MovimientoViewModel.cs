using System;

namespace SistemaControlPNP.Models
{
    public class MovimientoViewModel
    {
        public long IdMovimiento { get; set; }
        public int IdPersonal { get; set; }

        public string Grado { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Nombres { get; set; } = "";
        public string Unidad { get; set; } = "";

        public string Motivo { get; set; } = "";
        public string Autoriza { get; set; } = "";
        public string Destino { get; set; } = "";
        public string Novedades { get; set; } = "";

        // Nuevos: fechas locales (Lima)
        public DateTime FechaSalidaLocal { get; set; }
        public DateTime? FechaRetornoLocal { get; set; }
    }
}
