using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConfigSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "config_override_until",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "config_schedule_enabled",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "config_schedule_evaluated_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "config_schedule_fallback_profile_id",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "source_profile_id",
                table: "device_config_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_config_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    interval_s = table.Column<int>(type: "integer", nullable: false),
                    sleep_between = table.Column<bool>(type: "boolean", nullable: false),
                    fix_timeout_s = table.Column<int>(type: "integer", nullable: false),
                    queue_max_fixes = table.Column<int>(type: "integer", nullable: false),
                    retry_interval_h = table.Column<int>(type: "integer", nullable: false),
                    retry_max_age_h = table.Column<int>(type: "integer", nullable: false),
                    config_check_s = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_config_profiles", x => x.id);
                    table.CheckConstraint("ck_device_config_profiles_config_check_s", "config_check_s BETWEEN 60 AND 86400");
                    table.CheckConstraint("ck_device_config_profiles_fix_timeout_s", "fix_timeout_s BETWEEN 15 AND 3600");
                    table.CheckConstraint("ck_device_config_profiles_interval_s", "interval_s BETWEEN 5 AND 86400");
                    table.CheckConstraint("ck_device_config_profiles_queue_max_fixes", "queue_max_fixes BETWEEN 100 AND 100000");
                    table.CheckConstraint("ck_device_config_profiles_retry_interval_h", "retry_interval_h BETWEEN 1 AND 720");
                    table.CheckConstraint("ck_device_config_profiles_retry_max_age_h", "retry_max_age_h BETWEEN 0 AND 8760");
                    table.ForeignKey(
                        name: "FK_device_config_profiles_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_config_profiles_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_config_schedule_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days_mask_utc = table.Column<int>(type: "integer", nullable: false),
                    start_minute_utc = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_config_schedule_rules", x => x.id);
                    table.CheckConstraint("ck_device_config_schedule_rules_days_mask", "days_mask_utc BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_device_config_schedule_rules_duration", "duration_minutes BETWEEN 1 AND 1440");
                    table.CheckConstraint("ck_device_config_schedule_rules_priority", "priority BETWEEN 0 AND 1000");
                    table.CheckConstraint("ck_device_config_schedule_rules_start_minute", "start_minute_utc BETWEEN 0 AND 1439");
                    table.ForeignKey(
                        name: "FK_device_config_schedule_rules_device_config_profiles_profile~",
                        column: x => x.profile_id,
                        principalTable: "device_config_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_config_schedule_rules_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_devices_config_schedule_fallback_profile_id",
                table: "devices",
                column: "config_schedule_fallback_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_config_versions_source_profile_id",
                table: "device_config_versions",
                column: "source_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_config_profiles_created_by_user_id",
                table: "device_config_profiles",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_device_config_profiles_device_id_name",
                table: "device_config_profiles",
                columns: new[] { "device_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_config_schedule_rules_device_id",
                table: "device_config_schedule_rules",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_config_schedule_rules_profile_id",
                table: "device_config_schedule_rules",
                column: "profile_id");

            migrationBuilder.AddForeignKey(
                name: "FK_device_config_versions_device_config_profiles_source_profil~",
                table: "device_config_versions",
                column: "source_profile_id",
                principalTable: "device_config_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_devices_device_config_profiles_config_schedule_fallback_pro~",
                table: "devices",
                column: "config_schedule_fallback_profile_id",
                principalTable: "device_config_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_config_versions_device_config_profiles_source_profil~",
                table: "device_config_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_devices_device_config_profiles_config_schedule_fallback_pro~",
                table: "devices");

            migrationBuilder.DropTable(
                name: "device_config_schedule_rules");

            migrationBuilder.DropTable(
                name: "device_config_profiles");

            migrationBuilder.DropIndex(
                name: "IX_devices_config_schedule_fallback_profile_id",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_device_config_versions_source_profile_id",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "config_override_until",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "config_schedule_enabled",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "config_schedule_evaluated_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "config_schedule_fallback_profile_id",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "source",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "source_profile_id",
                table: "device_config_versions");
        }
    }
}
