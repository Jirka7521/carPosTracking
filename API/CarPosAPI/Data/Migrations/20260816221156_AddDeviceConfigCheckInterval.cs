using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConfigCheckInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default backfills every existing revision, so no separate seed
            // statement is needed here (unlike AddDeviceConfigVersions, which had to
            // create rows rather than fill a column).
            //
            // Deliberately no config_version bump. The new key's value is the default
            // everywhere, so nothing any device is actually running changes; the ingest
            // service's next reconnect re-publishes each document with the key added,
            // under the same version, and the firmware's merge decoder absorbs it.
            // Bumping would show the whole fleet as "pending" for a change that is not
            // one, which is exactly the signal the dashboard exists to keep honest.
            migrationBuilder.AddColumn<int>(
                name: "config_check_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 900);

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_config_check_s",
                table: "device_config_versions",
                sql: "config_check_s BETWEEN 60 AND 86400");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_config_check_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "config_check_s",
                table: "device_config_versions");
        }
    }
}
