using CompanionApp.Capture;
using CompanionApp.Models;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// ===== Cargar configuración
var cfg = builder.Configuration;

// ===== CORS
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.WithOrigins(cfg.GetSection("Companion:AllowedOrigins").Get<string[]>() ?? new[] { "*" })
         .AllowAnyMethod()
         .AllowAnyHeader());
});

// ===== Proveedor de captura según modo
var mode = cfg["Companion:Capture:Mode"] ?? "Demo";
switch (mode)
{
    case "ExternalProcess":
        builder.Services.AddSingleton<ICaptureProvider, ExternalProcessCaptureProvider>();
        break;
    // case "SupremaSdk": // Cuando integres el SDK, crea una clase y regístrala aquí
    //     builder.Services.AddSingleton<ICaptureProvider, SupremaSdkCaptureProvider>();
    //     break;
    default:
        builder.Services.AddSingleton<ICaptureProvider, DemoCaptureProvider>();
        break;
}

builder.Services.AddSingleton<CaptureService>();

// (Opcional) para correr como servicio en Windows
builder.Host.UseWindowsService();

var app = builder.Build();

// ===== Kestrel HTTPS
// Usará el dev-cert de dotnet si no configuraste PFX en appsettings.json
app.UseHttpsRedirection();

// ===== CORS
app.UseCors();

// ===== Endpoints
app.MapGet("/", () => new { ok = true, device = "Suprema RealScan-D Companion", ts = DateTime.UtcNow });

app.MapGet("/info", () => new
{
    ok = true,
    mode,
    origins = cfg.GetSection("Companion:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>(),
    ts = DateTime.UtcNow
});

// Captura principal
app.MapGet("/capture", async (CaptureService svc) =>
{
    try
    {
        var result = await svc.CaptureAsync();
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new CaptureResult { success = false, message = ex.Message });
    }
});

app.Run();
