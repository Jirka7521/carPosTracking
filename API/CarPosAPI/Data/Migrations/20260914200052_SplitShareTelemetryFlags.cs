using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitShareTelemetryFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One column gated battery and temperature together, so a creator who
            // only wanted to show that the tracker still had charge had to disclose
            // the cabin temperature to do it.
            //
            // Splitting it is a rename plus an addition rather than a drop plus two
            // additions, and that choice is the whole point of this migration: the
            // rename is what keeps every link live at deploy time disclosing exactly
            // what it disclosed the minute before. Nobody watches the readings vanish
            // half way through a two-hour share, and no creator has to re-make a
            // decision they already made.
            //
            // PostgreSQL's RENAME COLUMN carries the type, the NOT NULL and the
            // DEFAULT false across with the name, so the renamed column is complete
            // as it stands.
            migrationBuilder.RenameColumn(
                name: "include_telemetry",
                table: "share_links",
                newName: "include_battery");

            migrationBuilder.AddColumn<bool>(
                name: "include_temperature",
                table: "share_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // DEFAULT false is the right answer for a link created from now on and the
            // wrong one for a link created before: a share with telemetry switched on
            // was disclosing the temperature already, and this migration may no more
            // quietly withdraw that than it may quietly add it. Same transaction as
            // the two operations above, so the split lands whole or not at all.
            migrationBuilder.Sql(
                "UPDATE share_links SET include_temperature = include_battery;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Two flags collapsing back into one means one of them loses, and which
            // one is a privacy decision rather than a matter of taste. AND, not OR:
            // rolling back must never disclose more than the split schema did, and OR
            // would switch the temperature back on for a link whose creator had asked
            // for the battery alone. The accepted consequence is that a link holding
            // exactly one of the two comes back holding neither.
            migrationBuilder.Sql(
                "UPDATE share_links SET include_battery = include_battery AND include_temperature;");

            migrationBuilder.DropColumn(
                name: "include_temperature",
                table: "share_links");

            migrationBuilder.RenameColumn(
                name: "include_battery",
                table: "share_links",
                newName: "include_telemetry");
        }
    }
}
