using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listener.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticEmailResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultadoJson",
                table: "CorreosProcesados",
                type: "nvarchar(max)",
                maxLength: 16000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultadoJson",
                table: "CorreosProcesados");
        }
    }
}
