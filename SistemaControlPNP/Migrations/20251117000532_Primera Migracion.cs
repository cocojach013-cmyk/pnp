using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaControlPNP.Migrations
{
    /// <inheritdoc />
    public partial class PrimeraMigracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Personal",
                columns: table => new
                {
                    IdPersonal = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Grado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Nombres = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Apellidos = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NumeroCip = table.Column<string>(type: "varchar(8)", unicode: false, nullable: false),
                    Dni = table.Column<string>(type: "varchar(8)", unicode: false, nullable: false),
                    Telefono = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CorreoInstitucional = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Unidad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Foto = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: true),
                    Huella = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Sexo = table.Column<string>(type: "char(1)", unicode: false, nullable: false),
                    FechaNacimiento = table.Column<DateTime>(type: "date", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Estado = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personal", x => x.IdPersonal);
                    table.CheckConstraint("CK_Personal_CIP_Exact8Digits", "LEN([NumeroCip]) = 8 AND [NumeroCip] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_Personal_DNI_Exact8Digits", "LEN([Dni]) = 8 AND [Dni] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_Personal_FechaNacimiento_Rango", "[FechaNacimiento] >= '1900-01-01' AND [FechaNacimiento] <= CAST(SYSUTCDATETIME() AS date)");
                    table.CheckConstraint("CK_Personal_Sexo", "[Sexo] IN ('M','F')");
                });

            migrationBuilder.CreateTable(
                name: "Asistencia",
                columns: table => new
                {
                    IdAsistencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdPersonal = table.Column<int>(type: "int", nullable: false),
                    FechaIngreso = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FechaSalida = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Control_Asistencia = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asistencia", x => x.IdAsistencia);
                    table.CheckConstraint("CK_Asistencia_Orden", "([FechaSalida] IS NULL OR [FechaIngreso] <= [FechaSalida])");
                    table.ForeignKey(
                        name: "FK_Asistencia_Personal_IdPersonal",
                        column: x => x.IdPersonal,
                        principalTable: "Personal",
                        principalColumn: "IdPersonal",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Movimiento",
                columns: table => new
                {
                    IdMovimiento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdPersonal = table.Column<int>(type: "int", nullable: false),
                    FechaSalida = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Destino = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Autoriza = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FechaRetorno = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Novedades = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movimiento", x => x.IdMovimiento);
                    table.CheckConstraint("CK_Mov_OrdenHoras", "([FechaRetorno] IS NULL OR [FechaSalida] <= [FechaRetorno])");
                    table.ForeignKey(
                        name: "FK_Movimiento_Personal_IdPersonal",
                        column: x => x.IdPersonal,
                        principalTable: "Personal",
                        principalColumn: "IdPersonal",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdPersonal = table.Column<int>(type: "int", nullable: false),
                    UsuarioLogin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ContrasenaHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Rol = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Estado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.IdUsuario);
                    table.ForeignKey(
                        name: "FK_Usuarios_Personal_IdPersonal",
                        column: x => x.IdPersonal,
                        principalTable: "Personal",
                        principalColumn: "IdPersonal",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Auditoria",
                columns: table => new
                {
                    IdAuditoria = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: true),
                    Accion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RegistroID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IpUsuario = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InicioSesion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auditoria", x => x.IdAuditoria);
                    table.ForeignKey(
                        name: "FK_Auditoria_Usuarios",
                        column: x => x.IdUsuario,
                        principalTable: "Usuarios",
                        principalColumn: "IdUsuario");
                });

            migrationBuilder.InsertData(
                table: "Personal",
                columns: new[] { "IdPersonal", "Apellidos", "CorreoInstitucional", "Direccion", "Dni", "Estado", "FechaNacimiento", "FechaRegistro", "Foto", "Grado", "Huella", "Nombres", "NumeroCip", "Sexo", "Telefono", "Unidad" },
                values: new object[] { 1, "PEREZ GARCIA", "juan.perez@policia.gob.pe", "Av. Los Olivos 123", "87654321", true, new DateTime(1985, 5, 12, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "CAP. PNP", null, "JUAN", "12345678", "M", "999888777", "DIRINCRI" });

            migrationBuilder.CreateIndex(
                name: "IX_Asistencia_FechaIngreso",
                table: "Asistencia",
                column: "FechaIngreso");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencia_FechaSalida",
                table: "Asistencia",
                column: "FechaSalida");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencia_IdPersonal",
                table: "Asistencia",
                column: "IdPersonal");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencia_IdPersonal_FechaIngreso",
                table: "Asistencia",
                columns: new[] { "IdPersonal", "FechaIngreso" });

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_Accion",
                table: "Auditoria",
                column: "Accion");

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_FechaHora",
                table: "Auditoria",
                column: "FechaHora");

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_IdUsuario_FechaHora",
                table: "Auditoria",
                columns: new[] { "IdUsuario", "FechaHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Movimiento_FechaRetorno",
                table: "Movimiento",
                column: "FechaRetorno");

            migrationBuilder.CreateIndex(
                name: "IX_Movimiento_FechaSalida",
                table: "Movimiento",
                column: "FechaSalida");

            migrationBuilder.CreateIndex(
                name: "IX_Movimiento_IdPersonal",
                table: "Movimiento",
                column: "IdPersonal");

            migrationBuilder.CreateIndex(
                name: "IX_Personal_Apellidos",
                table: "Personal",
                column: "Apellidos");

            migrationBuilder.CreateIndex(
                name: "IX_Personal_CorreoInstitucional",
                table: "Personal",
                column: "CorreoInstitucional");

            migrationBuilder.CreateIndex(
                name: "IX_Personal_Dni",
                table: "Personal",
                column: "Dni",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Personal_Estado",
                table: "Personal",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Personal_NumeroCip",
                table: "Personal",
                column: "NumeroCip",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_IdPersonal",
                table: "Usuarios",
                column: "IdPersonal");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_UsuarioLogin",
                table: "Usuarios",
                column: "UsuarioLogin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Asistencia");

            migrationBuilder.DropTable(
                name: "Auditoria");

            migrationBuilder.DropTable(
                name: "Movimiento");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Personal");
        }
    }
}
