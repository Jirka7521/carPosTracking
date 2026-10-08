using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAmbientTemperatureAndHumidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The DHT22 ambient sensor has been removed from the hardware, the firmware
            // and every reader, so its two columns go with it. This PERMANENTLY DELETES
            // every stored ambient temperature and humidity reading - Down() restores the
            // empty columns, not the data. temperature_c (the modem's own die temperature)
            // is a different sensor and is untouched.
            //
            // Apply it only AFTER the API that no longer writes these columns is
            // deployed: the previous build's ingest INSERT names both of them and would
            // reject every fix against a table that no longer has them. Firmware still
            // sending ambient_temp_c / humidity_pct needs nothing - the ingest
            // deserializer skips unknown members.
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
