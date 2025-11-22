using System;
using Microsoft.EntityFrameworkCore;
using SistemaControlPNP.Models;

namespace ControlPNP.Data
{
    public class ControlDBContext : DbContext
    {
        public ControlDBContext(DbContextOptions<ControlDBContext> options) : base(options) { }

        // DbSets (sintaxis de expresión)
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Personal> Personal => Set<Personal>();
        public DbSet<Asistencia> Asistencia => Set<Asistencia>();
        public DbSet<Movimiento> Movimiento => Set<Movimiento>();
        public DbSet<Auditoria> Auditoria => Set<Auditoria>(); // Auditoría

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ========== Personal ==========
            modelBuilder.Entity<Personal>(tb =>
            {
                tb.ToTable("Personal");
                tb.HasKey(x => x.IdPersonal);

                tb.Property(x => x.Grado).HasMaxLength(30);
                tb.Property(x => x.Nombres).IsRequired().HasMaxLength(50);
                tb.Property(x => x.Apellidos).IsRequired().HasMaxLength(80);

                tb.Property(x => x.NumeroCip)
                  .IsRequired()
                  .HasColumnType("varchar(8)")
                  .IsUnicode(false);
                tb.HasIndex(x => x.NumeroCip).IsUnique();
                tb.HasCheckConstraint("CK_Personal_CIP_Exact8Digits",
                    "LEN([NumeroCip]) = 8 AND [NumeroCip] NOT LIKE '%[^0-9]%'");

                tb.Property(x => x.Dni)
                  .IsRequired()
                  .HasColumnType("varchar(8)")
                  .IsUnicode(false);
                tb.HasIndex(x => x.Dni).IsUnique();
                tb.HasCheckConstraint("CK_Personal_DNI_Exact8Digits",
                    "LEN([Dni]) = 8 AND [Dni] NOT LIKE '%[^0-9]%'");

                tb.Property(x => x.Telefono).HasMaxLength(15).IsUnicode(false);
                tb.Property(x => x.Direccion).HasMaxLength(200);
                tb.Property(x => x.Unidad).HasMaxLength(120);
                tb.Property(x => x.CorreoInstitucional).HasMaxLength(100).IsUnicode(false);

                tb.Property(x => x.Foto).HasMaxLength(512).IsUnicode(false);
                tb.Property(x => x.Huella).HasColumnType("varbinary(max)");

                tb.Property(x => x.Sexo)
                  .IsRequired()
                  .HasColumnType("char(1)")
                  .IsUnicode(false);
                tb.HasCheckConstraint("CK_Personal_Sexo", "[Sexo] IN ('M','F')");

                tb.Property(x => x.FechaNacimiento)
                  .HasColumnType("date")
                  .IsRequired();
                tb.HasCheckConstraint("CK_Personal_FechaNacimiento_Rango",
                    "[FechaNacimiento] >= '1900-01-01' AND [FechaNacimiento] <= CAST(SYSUTCDATETIME() AS date)");

                tb.Property(x => x.FechaRegistro)
                  .HasColumnType("datetime2(3)")
                  .HasDefaultValueSql("SYSUTCDATETIME()");

                tb.Property(x => x.Estado).IsRequired();

                tb.Property<byte[]>("RowVersion").IsRowVersion();

                // Índices: SIEMPRE sobre la entidad (x => x.Propiedad)
                tb.HasIndex(x => x.Apellidos);
                tb.HasIndex(x => x.Estado);
                tb.HasIndex(x => x.CorreoInstitucional);

                // Seed opcional
                tb.HasData(new Personal
                {
                    IdPersonal = 1,
                    Grado = "CAP. PNP",
                    Nombres = "JUAN",
                    Apellidos = "PEREZ GARCIA",
                    NumeroCip = "12345678",
                    Dni = "87654321",
                    Telefono = "999888777",
                    Direccion = "Av. Los Olivos 123",
                    Unidad = "DIRINCRI",
                    CorreoInstitucional = "juan.perez@policia.gob.pe",
                    Foto = null,
                    Huella = null,
                    Sexo = "M",
                    FechaNacimiento = new DateTime(1985, 5, 12),
                    FechaRegistro = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Estado = true
                });
            });

            // ========== Usuario ==========
            modelBuilder.Entity<Usuario>(tb =>
            {
                tb.ToTable("Usuarios");
                tb.HasKey(x => x.IdUsuario);

                tb.Property(x => x.UsuarioLogin)
                  .IsRequired()
                  .HasMaxLength(32)
                  .UseCollation("Latin1_General_100_BIN2");

                tb.Property(x => x.ContrasenaHash)
                  .IsRequired()
                  .HasMaxLength(256)
                  .UseCollation("Latin1_General_100_BIN2");

                tb.Property(x => x.Rol)
                  .IsRequired()
                  .HasMaxLength(20)
                  .IsUnicode(false);

                tb.Property(x => x.Estado).IsRequired();

                tb.HasIndex(x => x.UsuarioLogin).IsUnique();

                tb.Property(x => x.IdPersonal).IsRequired();
                tb.HasOne(x => x.Personal)
                  .WithMany()
                  .HasForeignKey(x => x.IdPersonal)
                  .OnDelete(DeleteBehavior.Restrict);
            });

            // ========== Auditoria ==========
            modelBuilder.Entity<Auditoria>(e =>
            {
                e.ToTable("Auditoria");
                e.HasKey(x => x.IdAuditoria);

                e.Property(x => x.Accion).HasMaxLength(64).IsRequired();
                e.Property(x => x.RegistroID).HasMaxLength(128);
                e.Property(x => x.Descripcion).HasMaxLength(1024);
                e.Property(x => x.IpUsuario).HasMaxLength(64);
                e.Property(x => x.FechaHora).IsRequired();

                e.HasOne(x => x.UsuarioRef)
                 .WithMany()
                 .HasForeignKey(x => x.IdUsuario)
                 .HasConstraintName("FK_Auditoria_Usuarios")
                 .OnDelete(DeleteBehavior.NoAction);

                e.HasIndex(x => x.FechaHora);
                e.HasIndex(x => x.Accion);
                e.HasIndex(x => new { x.IdUsuario, x.FechaHora });
            });

            // ========== Asistencia ==========
            modelBuilder.Entity<Asistencia>(tb =>
            {
                tb.ToTable("Asistencia");
                tb.HasKey(x => x.IdAsistencia);

                tb.Property(x => x.IdPersonal).IsRequired();
                tb.Property(x => x.FechaIngreso).HasColumnType("datetime2(3)").IsRequired();
                tb.Property(x => x.FechaSalida).HasColumnType("datetime2(3)");
                tb.Property(x => x.Control_Asistencia).HasMaxLength(20).IsUnicode(false);

                tb.HasOne(x => x.Personal)
                  .WithMany()
                  .HasForeignKey(x => x.IdPersonal)
                  .OnDelete(DeleteBehavior.Cascade);

                tb.HasCheckConstraint("CK_Asistencia_Orden",
                    "([FechaSalida] IS NULL OR [FechaIngreso] <= [FechaSalida])");

                tb.HasIndex(x => x.IdPersonal);
                tb.HasIndex(x => x.FechaIngreso);
                tb.HasIndex(x => x.FechaSalida);
                tb.HasIndex(x => new { x.IdPersonal, x.FechaIngreso });
            });

            // ========== Movimiento ==========
            modelBuilder.Entity<Movimiento>(tb =>
            {
                tb.ToTable("Movimiento");
                tb.HasKey(x => x.IdMovimiento);

                tb.Property(x => x.IdPersonal).IsRequired();
                tb.Property(x => x.FechaSalida).HasColumnType("datetime2(3)").IsRequired();
                tb.Property(x => x.FechaRetorno).HasColumnType("datetime2(3)");
                tb.Property(x => x.Motivo).IsRequired().HasMaxLength(200);
                tb.Property(x => x.Destino).HasMaxLength(200);
                tb.Property(x => x.Autoriza).IsRequired().HasMaxLength(120);
                tb.Property(x => x.Novedades).HasColumnType("nvarchar(max)");

                tb.HasOne(x => x.Personal)
                  .WithMany()
                  .HasForeignKey(x => x.IdPersonal)
                  .OnDelete(DeleteBehavior.Cascade);

                tb.HasCheckConstraint("CK_Mov_OrdenHoras",
                    "([FechaRetorno] IS NULL OR [FechaSalida] <= [FechaRetorno])");

                tb.HasIndex(x => x.FechaSalida);
                tb.HasIndex(x => x.FechaRetorno);
            });
        }
    }
}
