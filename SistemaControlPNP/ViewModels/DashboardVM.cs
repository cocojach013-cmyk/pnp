using System;
using System.Collections.Generic;

namespace ControlPNP.ViewModels
{
    public class DashboardVM
    {
        // KPIs
        public int PersonalActivos { get; set; }
        public int UsuariosActivos { get; set; }
        public int AsistenciasHoy { get; set; }
        public int MovimientosAbiertos { get; set; }

        // Listas
        public List<UltimaAsistenciaVM> UltimasAsistencias { get; set; } = new();
        public List<UltimoMovimientoVM> UltimosMovimientos { get; set; } = new();
        public List<ActividadVM> UltimasAcciones { get; set; } = new();
    }

    public class UltimaAsistenciaVM
    {
        public DateTime Ingreso { get; set; }
        public DateTime? Salida { get; set; }
        public string PersonalNombre { get; set; } = "";
        public string? Unidad { get; set; }
    }

    public class UltimoMovimientoVM
    {
        public DateTime Salida { get; set; }
        public DateTime? Retorno { get; set; }
        public string Motivo { get; set; } = "";
        public string? Destino { get; set; }
        public string PersonalNombre { get; set; } = "";
    }

    public class ActividadVM
    {
        public DateTime FechaHora { get; set; }
        public string Accion { get; set; } = "";
        public string? Descripcion { get; set; }
        public string Usuario { get; set; } = "";
        public string? Ip { get; set; }
    }
}
