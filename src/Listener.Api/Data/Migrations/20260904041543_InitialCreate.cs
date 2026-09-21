using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listener.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EstadoSincronizacionCorreo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Buzon = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Carpeta = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: true),
                    UltimoUidProcesado = table.Column<long>(type: "bigint", nullable: false),
                    UltimaSincronizacionCorrecta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionFila = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadoSincronizacionCorreo", x => x.Id);
                    table.CheckConstraint("CK_Sincronizacion_UidValidity", "[UidValidity] IS NULL OR [UidValidity] BETWEEN 1 AND 4294967295");
                    table.CheckConstraint("CK_Sincronizacion_UltimoUid", "[UltimoUidProcesado] BETWEEN 0 AND 4294967295 AND ([UidValidity] IS NOT NULL OR [UltimoUidProcesado] = 0)");
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesVerificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenciaExterna = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReferenciaConductor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MontoEsperado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    MedioPago = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenciaCuentaReceptora = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OperacionDeclarada = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaCreacion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaExpiracion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaResolucion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionFila = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesVerificacion", x => x.Id);
                    table.CheckConstraint("CK_Solicitudes_Estado", "[Estado] IN ('Pendiente','Verificada','Rechazada','RequiereRevision','Expirada')");
                    table.CheckConstraint("CK_Solicitudes_Fechas", "[FechaExpiracion] > [FechaCreacion] AND ([FechaResolucion] IS NULL OR [FechaResolucion] >= [FechaCreacion])");
                    table.CheckConstraint("CK_Solicitudes_Moneda", "LEN([Moneda]) = 3");
                    table.CheckConstraint("CK_Solicitudes_Monto", "[MontoEsperado] > 0");
                    table.CheckConstraint("CK_Solicitudes_Resolucion", "([Estado] IN ('Pendiente','RequiereRevision') AND [FechaResolucion] IS NULL) OR ([Estado] IN ('Verificada','Rechazada','Expirada') AND [FechaResolucion] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "CorreosProcesados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstadoSincronizacionCorreoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UidValidity = table.Column<long>(type: "bigint", nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(998)", maxLength: 998, nullable: true),
                    Remitente = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Asunto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FechaRecepcion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FechaRegistro = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HashContenido = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReferenciaContenidoProtegido = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResultadoAutenticidad = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EstadoProcesamiento = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VersionExtractor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VersionFila = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreosProcesados", x => x.Id);
                    table.CheckConstraint("CK_Correos_Autenticidad", "[ResultadoAutenticidad] IN ('NoVerificada','Valida','Invalida')");
                    table.CheckConstraint("CK_Correos_Estado", "[EstadoProcesamiento] IN ('Pendiente','Procesado','Descartado','Error')");
                    table.CheckConstraint("CK_Correos_Hash", "LEN([HashContenido]) = 64 AND [HashContenido] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-fA-F]%'");
                    table.CheckConstraint("CK_Correos_Uids", "[UidValidity] BETWEEN 1 AND 4294967295 AND [Uid] BETWEEN 1 AND 4294967295");
                    table.ForeignKey(
                        name: "FK_CorreosProcesados_EstadoSincronizacionCorreo_EstadoSincronizacionCorreoId",
                        column: x => x.EstadoSincronizacionCorreoId,
                        principalTable: "EstadoSincronizacionCorreo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventosAuditoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SolicitudVerificacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorreoProcesadoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TipoEvento = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EstadoAnterior = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EstadoNuevo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdCorrelacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAuditoria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosAuditoria_CorreosProcesados_CorreoProcesadoId",
                        column: x => x.CorreoProcesadoId,
                        principalTable: "CorreosProcesados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventosAuditoria_SolicitudesVerificacion_SolicitudVerificacionId",
                        column: x => x.SolicitudVerificacionId,
                        principalTable: "SolicitudesVerificacion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenciasPago",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorreoProcesadoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProveedorPago = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NumeroOperacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenciaCuentaReceptora = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Moneda = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    FechaOperacion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReferenciaPagador = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DireccionMovimiento = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EstadoPagoInformado = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EstadoValidacion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SolicitudVerificacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FechaRegistro = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VersionFila = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenciasPago", x => x.Id);
                    table.CheckConstraint("CK_Evidencias_Asignacion", "[SolicitudVerificacionId] IS NULL OR [EstadoValidacion] = 'Validada'");
                    table.CheckConstraint("CK_Evidencias_Direccion", "[DireccionMovimiento] IN ('Desconocida','Ingreso','Salida')");
                    table.CheckConstraint("CK_Evidencias_Estado", "[EstadoValidacion] IN ('Pendiente','Validada','Invalida','Duplicada','RequiereRevision')");
                    table.CheckConstraint("CK_Evidencias_EstadoPago", "[EstadoPagoInformado] IN ('Desconocido','Pendiente','Completado','Fallido','Reversado')");
                    table.CheckConstraint("CK_Evidencias_Moneda", "[Moneda] IS NULL OR LEN([Moneda]) = 3");
                    table.CheckConstraint("CK_Evidencias_Monto", "[Monto] IS NULL OR [Monto] > 0");
                    table.CheckConstraint("CK_Evidencias_ValidadaCompleta", "[EstadoValidacion] <> 'Validada' OR ([ProveedorPago] IS NOT NULL AND LEN([ProveedorPago]) > 0 AND [NumeroOperacion] IS NOT NULL AND LEN([NumeroOperacion]) > 0 AND [ReferenciaCuentaReceptora] IS NOT NULL AND LEN([ReferenciaCuentaReceptora]) > 0 AND [Monto] IS NOT NULL AND [Moneda] IS NOT NULL AND [FechaOperacion] IS NOT NULL AND [DireccionMovimiento] = 'Ingreso' AND [EstadoPagoInformado] = 'Completado')");
                    table.ForeignKey(
                        name: "FK_EvidenciasPago_CorreosProcesados_CorreoProcesadoId",
                        column: x => x.CorreoProcesadoId,
                        principalTable: "CorreosProcesados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenciasPago_SolicitudesVerificacion_SolicitudVerificacionId",
                        column: x => x.SolicitudVerificacionId,
                        principalTable: "SolicitudesVerificacion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorreosProcesados_EstadoProcesamiento_FechaRecepcion",
                table: "CorreosProcesados",
                columns: new[] { "EstadoProcesamiento", "FechaRecepcion" });

            migrationBuilder.CreateIndex(
                name: "IX_CorreosProcesados_EstadoSincronizacionCorreoId_UidValidity_Uid",
                table: "CorreosProcesados",
                columns: new[] { "EstadoSincronizacionCorreoId", "UidValidity", "Uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorreosProcesados_HashContenido",
                table: "CorreosProcesados",
                column: "HashContenido");

            migrationBuilder.CreateIndex(
                name: "IX_EstadoSincronizacionCorreo_Buzon_Carpeta",
                table: "EstadoSincronizacionCorreo",
                columns: new[] { "Buzon", "Carpeta" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_CorreoProcesadoId",
                table: "EventosAuditoria",
                column: "CorreoProcesadoId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_Fecha",
                table: "EventosAuditoria",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_IdCorrelacion",
                table: "EventosAuditoria",
                column: "IdCorrelacion");

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_SolicitudVerificacionId",
                table: "EventosAuditoria",
                column: "SolicitudVerificacionId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenciasPago_CorreoProcesadoId",
                table: "EvidenciasPago",
                column: "CorreoProcesadoId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenciasPago_EstadoValidacion_FechaOperacion",
                table: "EvidenciasPago",
                columns: new[] { "EstadoValidacion", "FechaOperacion" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenciasPago_ProveedorPago_ReferenciaCuentaReceptora_NumeroOperacion",
                table: "EvidenciasPago",
                columns: new[] { "ProveedorPago", "ReferenciaCuentaReceptora", "NumeroOperacion" },
                unique: true,
                filter: "[EstadoValidacion] = 'Validada'");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenciasPago_SolicitudVerificacionId",
                table: "EvidenciasPago",
                column: "SolicitudVerificacionId",
                unique: true,
                filter: "[SolicitudVerificacionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesVerificacion_Estado_FechaExpiracion",
                table: "SolicitudesVerificacion",
                columns: new[] { "Estado", "FechaExpiracion" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesVerificacion_ReferenciaExterna",
                table: "SolicitudesVerificacion",
                column: "ReferenciaExterna",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosAuditoria");

            migrationBuilder.DropTable(
                name: "EvidenciasPago");

            migrationBuilder.DropTable(
                name: "CorreosProcesados");

            migrationBuilder.DropTable(
                name: "SolicitudesVerificacion");

            migrationBuilder.DropTable(
                name: "EstadoSincronizacionCorreo");
        }
    }
}
