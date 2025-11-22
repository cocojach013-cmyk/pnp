using System;

namespace SistemaControlPNP.Models
{
    public class Asistencia
    {
        public long IdAsistencia { get; set; }
        public int IdPersonal { get; set; }          // FK a Personal
        public DateTime FechaIngreso { get; set; }        // Fecha y hora de ingreso
        public DateTime? FechaSalida { get; set; }        // Fecha y hora de salida (nullable)
        public string Control_Asistencia { get; set; }  // temprano / tarde
        // Relación con Personal (opcional, para EF Core)
        public Personal Personal { get; set; }
    }
}
