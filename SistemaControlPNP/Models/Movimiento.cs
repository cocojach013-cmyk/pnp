using System;

namespace SistemaControlPNP.Models
{
    public class Movimiento
    {
        public long IdMovimiento { get; set; }
        public int IdPersonal { get; set; }            // FK a Personal
        public DateTime FechaSalida { get; set; }  // Hora y Fecha de Salida
        public string Motivo { get; set; }
        public string Destino { get; set; }
        public string Autoriza { get; set; }
        public DateTime? FechaRetorno { get; set; }     // null si aún no retorna
        public string Novedades { get; set; }
        // Relación con Personal (opcional, para EF Core)
        public Personal Personal { get; set; }
    }
}
