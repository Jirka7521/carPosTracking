using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemperature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "temperature_c",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_temperature_c",
                table: "positions",
                sql: "temperature_c >= -40 AND temperature_c <= 125");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_temperature_c",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "temperature_c",
                table: "positions");
        }
    }
}
