using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConfigVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "config_applied_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_applied_version",
                table: "devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_version",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "device_config_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    interval_s = table.Column<int>(type: "integer", nullable: false),
                    sleep_between = table.Column<bool>(type: "boolean", nullable: false),
                    fix_timeout_s = table.Column<int>(type: "integer", nullable: false),
                    queue_max_fixes = table.Column<int>(type: "integer", nullable: false),
                    retry_interval_h = table.Column<int>(type: "integer", nullable: false),
                    retry_max_age_h = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_config_versions", x => x.id);
                    table.CheckConstraint("ck_device_config_versions_fix_timeout_s", "fix_timeout_s BETWEEN 15 AND 900");
                    table.CheckConstraint("ck_device_config_versions_interval_s", "interval_s BETWEEN 5 AND 86400");
                    table.CheckConstraint("ck_device_config_versions_queue_max_fixes", "queue_max_fixes BETWEEN 100 AND 100000");
                    table.CheckConstraint("ck_device_config_versions_retry_interval_h", "retry_interval_h BETWEEN 1 AND 720");
                    table.CheckConstraint("ck_device_config_versions_retry_max_age_h", "retry_max_age_h BETWEEN 0 AND 8760");
                    table.CheckConstraint("ck_device_config_versions_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_device_config_versions_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_config_versions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_config_versions_created_by_user_id",
                table: "device_config_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_device_config_versions_device_id_version",
                table: "device_config_versions",
                columns: new[] { "device_id", "version" },
                unique: true);

            // Hand-written: give every device that already exists its revision 1, built
            // from the firmware's factory defaults (Dtos/DeviceConfigRules, which mirror
            // ESP32/src/config/Config.h).
            //
            // This is not optional tidying. devices.config_version defaults to 1 for
            // every existing row, and both the settings endpoints and the retained-config
            // sweep resolve that number to a device_config_versions row — without this
            // insert they would find nothing and the settings panel would 404 on every
            // device provisioned before this migration.
            //
            // created_by_user_id is left NULL: these are defaults, not somebody's choice.
            // No rows are re-published here; the ingest service's next connect does that.
            migrationBuilder.Sql(
                """
                INSERT INTO device_config_versions
                    (device_id, version, interval_s, sleep_between, fix_timeout_s,
                     queue_max_fixes, retry_interval_h, retry_max_age_h, created_by_user_id)
                SELECT d.id, 1, 60, false, 180, 20000, 24, 168, NULL
                FROM devices AS d
                ON CONFLICT (device_id, version) DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "config_applied_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "config_applied_version",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "config_version",
                table: "devices");
        }
    }
}
