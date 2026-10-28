using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listener.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBbvaPaymentAccumulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BbvaPaymentBalances",
                columns: table => new
                {
                    PayerKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PendingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    VersionFila = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbvaPaymentBalances", x => x.PayerKey);
                    table.CheckConstraint("CK_BbvaBalance_Pending", "[PendingAmount] >= 0 AND [PendingAmount] < 10");
                });

            migrationBuilder.CreateTable(
                name: "BbvaPaymentValidations",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AccumulatedAmount = table.Column<decimal>(type: "decimal(20,2)", precision: 20, scale: 2, nullable: false),
                    Correct = table.Column<bool>(type: "bit", nullable: false),
                    NotificationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbvaPaymentValidations", x => x.EventId);
                    table.CheckConstraint("CK_BbvaValidation_Amounts", "[ReceivedAmount] > 0 AND [AccumulatedAmount] >= [ReceivedAmount]");
                    table.CheckConstraint("CK_BbvaValidation_Correct", "([Correct] = 1 AND [AccumulatedAmount] >= 10) OR ([Correct] = 0 AND [AccumulatedAmount] < 10)");
                    table.ForeignKey(
                        name: "FK_BbvaPaymentValidations_BbvaPaymentBalances_PayerKey",
                        column: x => x.PayerKey,
                        principalTable: "BbvaPaymentBalances",
                        principalColumn: "PayerKey",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BbvaPaymentValidations_CorreosProcesados_EventId",
                        column: x => x.EventId,
                        principalTable: "CorreosProcesados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BbvaPaymentValidations_PayerKey_Sequence",
                table: "BbvaPaymentValidations",
                columns: new[] { "PayerKey", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BbvaPaymentValidations");

            migrationBuilder.DropTable(
                name: "BbvaPaymentBalances");
        }
    }
}
