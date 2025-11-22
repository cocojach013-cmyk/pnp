using System;
using ControlPNP.Data;
using ControlPNP.Filters;
using ControlPNP.Service;      // si ya no usas este namespace, lo puedes borrar
using ControlPNP.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ============================================
//  CONEXIÓN A BD
// ============================================
var connectionString = builder.Configuration.GetConnectionString("CadenaSQL")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:CadenaSQL en appsettings.json");

builder.Services.AddDbContext<ControlDBContext>(options =>
    options.UseSqlServer(connectionString));

// ============================================
//  SERVICIOS COMUNES
// ============================================
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddScoped<IAuditoriaWriter, AuditoriaWriter>();

builder.Services.AddSingleton<IPasswordService, PasswordService>();

// Servicio de auditoría (una sola vez)
builder.Services.AddScoped<IAuditService, AuditService>();

// Filtro global de auditoría
builder.Services.AddScoped<GlobalAuditFilter>();

// ============================================
//  SERVICIO BIOMÉTRICO (LECTOR ZK)
// ============================================
// Solo uno: usamos ZkBiometriaService como implementación de IBiometriaService
builder.Services.AddSingleton<IBiometriaService, ZkBiometriaService>();
// 🔹 Si ya no vas a usar SupremaRealScanService, elimina su clase o úsala con otra interfaz distinta.

// ============================================
//  MVC + AUTORIZACIÓN GLOBAL + AUDITORÍA
// ============================================
builder.Services.AddControllersWithViews(options =>
{
    // Política: todo requiere usuario autenticado por defecto
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(policy));

    // Filtro de auditoría global
    options.Filters.Add<GlobalAuditFilter>();
});

// ============================================
//  AUTENTICACIÓN POR COOKIES
// ============================================
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Index";
        options.AccessDeniedPath = "/Acceso/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    });

// ============================================
//  CONSTRUIR APP
// ============================================
var app = builder.Build();

// ============================================
//  PIPELINE HTTP
// ============================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Ruta por defecto
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
