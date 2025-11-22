using System;

namespace SistemaControlPNP.Models
{
    public class AsistenciaViewModel
    {
        public long IdAsistencia { get; set; }       // Id único
        public int IdPersonal { get; set; }          // FK

        public string Grado { get; set; } = "";
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Unidad { get; set; } = "";

        public DateTime FechaIngreso { get; set; }
        public DateTime? FechaSalida { get; set; }

        public string Control_Asistencia { get; set; } = ""; // visible o hidden, según lo uses

        public string Asistencia =>
            FechaIngreso.TimeOfDay <= new TimeSpan(7, 45, 59)
            ? "Presente"
            : "Tarde";

        // Formato string (para la vista)
        public string FechaIngresoStr => FechaIngreso.ToString("dd/MM/yyyy");
        public string HoraIngresoStr => FechaIngreso.ToString("HH:mm");

        public string? FechaSalidaStr => FechaSalida?.ToString("dd/MM/yyyy");
        public string? HoraSalidaStr => FechaSalida?.ToString("HH:mm");
    }
}
