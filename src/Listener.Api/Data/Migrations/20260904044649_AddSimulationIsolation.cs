using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listener.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsSimulacion",
                table: "SolicitudesVerificacion",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EsSimulacion",
                table: "EvidenciasPago",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReferenciaSolicitudExtraida",
                table: "EvidenciasPago",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsSimulacion",
                table: "CorreosProcesados",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Solicitudes_Simulacion",
                table: "SolicitudesVerificacion",
                sql: "[EsSimulacion] = 0 OR [Estado] <> 'Verificada'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Evidencias_Simulacion",
                table: "EvidenciasPago",
                sql: "[EsSimulacion] = 0 OR ([EstadoValidacion] <> 'Validada' AND [SolicitudVerificacionId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Correos_Simulacion",
                table: "CorreosProcesados",
                sql: "[EsSimulacion] = 0 OR [ResultadoAutenticidad] = 'NoVerificada'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Solicitudes_Simulacion",
                table: "SolicitudesVerificacion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evidencias_Simulacion",
                table: "EvidenciasPago");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Correos_Simulacion",
                table: "CorreosProcesados");

            migrationBuilder.DropColumn(
                name: "EsSimulacion",
                table: "SolicitudesVerificacion");

            migrationBuilder.DropColumn(
                name: "EsSimulacion",
                table: "EvidenciasPago");

            migrationBuilder.DropColumn(
                name: "ReferenciaSolicitudExtraida",
                table: "EvidenciasPago");

            migrationBuilder.DropColumn(
                name: "EsSimulacion",
                table: "CorreosProcesados");
        }
    }
}
