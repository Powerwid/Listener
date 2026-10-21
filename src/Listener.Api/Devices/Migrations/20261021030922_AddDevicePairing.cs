using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listener.Api.Devices.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListenerCollectorDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    NotificationDeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CredentialHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    LinkedAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListenerCollectorDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListenerDevicePairingTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListenerDevicePairingTickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListenerCollectorDevices_InstallationId",
                table: "ListenerCollectorDevices",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListenerDevicePairingTickets_ExpiresAt",
                table: "ListenerDevicePairingTickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ListenerDevicePairingTickets_TokenHash",
                table: "ListenerDevicePairingTickets",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListenerCollectorDevices");

            migrationBuilder.DropTable(
                name: "ListenerDevicePairingTickets");
        }
    }
}
