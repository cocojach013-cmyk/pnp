namespace ControlPNP.Services
{
    public class BiometriaTestResult
    {
        public bool Ok { get; set; }
        public string Mensaje { get; set; } = string.Empty;

        public int Dispositivos { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
