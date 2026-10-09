using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveModemTemperature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The SIM7000 die temperature (AT+CPMUTEMP) is no longer read by the
            // firmware, nor shown, shared or exported anywhere, so its column goes -
            // and with it the share-link opt-in that only ever gated that column. This
            // PERMANENTLY DELETES every stored modem temperature reading - Down()
            // restores the empty columns, not the data. Links that had the opt-in on
            // simply stop carrying a field nobody receives any more.
            //
            // Apply it only AFTER the API that no longer reads or writes these columns
            // is deployed: the previous build's ingest INSERT names temperature_c and
            // its share queries select include_temperature, so they would fail against
            // a table without them. Firmware still sending temp_c needs nothing - the
            // ingest deserializer skips unknown members.
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_temperature_c",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "include_temperature",
                table: "share_links");

            migrationBuilder.DropColumn(
                name: "temperature_c",
                table: "positions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "include_temperature",
                table: "share_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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
    }
}
