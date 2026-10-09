using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAmbientTemperatureAndHumidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ambient_temperature_c",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "humidity_pct",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_ambient_temperature_c",
                table: "positions",
                sql: "ambient_temperature_c >= -40 AND ambient_temperature_c <= 80");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_humidity_pct",
                table: "positions",
                sql: "humidity_pct >= 0 AND humidity_pct <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_ambient_temperature_c",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_humidity_pct",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "ambient_temperature_c",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "humidity_pct",
                table: "positions");
        }
    }
}
