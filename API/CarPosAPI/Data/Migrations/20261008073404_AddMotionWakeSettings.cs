using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMotionWakeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Twelve columns on each of device_config_versions and
            // device_config_profiles: the motion-wake switch and its four parameters,
            // plus a full MOVING copy of the seven runtime settings. The existing seven
            // columns are untouched and become the STANDBY set.
            //
            // The column defaults backfill every existing row, so no separate seed
            // statement is needed. They are the firmware's defaults, applied to the
            // profile table as well, so an existing profile comes out motion-off with the
            // firmware's own moving values. motion_enabled defaulting to false is what
            // makes this migration inert for every device that already exists.
            //
            // Deliberately no config_version bump. Motion is off by default and the
            // moving set is only ever consulted when it is on, so nothing any device is
            // actually running changes; the ingest service's next reconnect re-publishes
            // each document with the new "motion" object added, under the same version,
            // and the firmware's merge decoder absorbs it. Bumping would show the whole
            // fleet as "pending" for a change that is not one, which is exactly the
            // signal the dashboard exists to keep honest.
            //
            // Every numeric column gets a CHECK constraint built from DeviceConfigRules,
            // named like the existing ones. The moving set is bounded by the standby
            // set's limits, so those constraints repeat the standby ranges.
            migrationBuilder.AddColumn<bool>(
                name: "motion_enabled",
                table: "device_config_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "motion_speed_kmph",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 600);

            migrationBuilder.AddColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 63);

            migrationBuilder.AddColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "moving_config_check_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 3600);

            migrationBuilder.AddColumn<int>(
                name: "moving_fix_timeout_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AddColumn<int>(
                name: "moving_interval_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "moving_queue_max_fixes",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 20000);

            migrationBuilder.AddColumn<int>(
                name: "moving_retry_interval_h",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "moving_retry_max_age_h",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 168);

            migrationBuilder.AddColumn<bool>(
                name: "moving_sleep_between",
                table: "device_config_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "motion_enabled",
                table: "device_config_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "motion_speed_kmph",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 600);

            migrationBuilder.AddColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 63);

            migrationBuilder.AddColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "moving_config_check_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 3600);

            migrationBuilder.AddColumn<int>(
                name: "moving_fix_timeout_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AddColumn<int>(
                name: "moving_interval_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "moving_queue_max_fixes",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 20000);

            migrationBuilder.AddColumn<int>(
                name: "moving_retry_interval_h",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "moving_retry_max_age_h",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 168);

            migrationBuilder.AddColumn<bool>(
                name: "moving_sleep_between",
                table: "device_config_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_motion_speed_kmph",
                table: "device_config_versions",
                sql: "motion_speed_kmph BETWEEN 1 AND 50");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_motion_stop_wait_s",
                table: "device_config_versions",
                sql: "motion_stop_wait_s BETWEEN 60 AND 7200");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_motion_threshold_mg",
                table: "device_config_versions",
                sql: "motion_threshold_mg BETWEEN 63 AND 2000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_motion_wake_wait_s",
                table: "device_config_versions",
                sql: "motion_wake_wait_s BETWEEN 30 AND 3600");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_config_check_s",
                table: "device_config_versions",
                sql: "moving_config_check_s BETWEEN 60 AND 86400");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_fix_timeout_s",
                table: "device_config_versions",
                sql: "moving_fix_timeout_s BETWEEN 15 AND 3600");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_interval_s",
                table: "device_config_versions",
                sql: "moving_interval_s BETWEEN 5 AND 86400");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_queue_max_fixes",
                table: "device_config_versions",
                sql: "moving_queue_max_fixes BETWEEN 100 AND 100000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_retry_interval_h",
                table: "device_config_versions",
                sql: "moving_retry_interval_h BETWEEN 1 AND 720");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_retry_max_age_h",
                table: "device_config_versions",
                sql: "moving_retry_max_age_h BETWEEN 0 AND 8760");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_motion_speed_kmph",
                table: "device_config_profiles",
                sql: "motion_speed_kmph BETWEEN 1 AND 50");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_motion_stop_wait_s",
                table: "device_config_profiles",
                sql: "motion_stop_wait_s BETWEEN 60 AND 7200");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_motion_threshold_mg",
                table: "device_config_profiles",
                sql: "motion_threshold_mg BETWEEN 63 AND 2000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_motion_wake_wait_s",
                table: "device_config_profiles",
                sql: "motion_wake_wait_s BETWEEN 30 AND 3600");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_config_check_s",
                table: "device_config_profiles",
                sql: "moving_config_check_s BETWEEN 60 AND 86400");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_fix_timeout_s",
                table: "device_config_profiles",
                sql: "moving_fix_timeout_s BETWEEN 15 AND 3600");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_interval_s",
                table: "device_config_profiles",
                sql: "moving_interval_s BETWEEN 5 AND 86400");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_queue_max_fixes",
                table: "device_config_profiles",
                sql: "moving_queue_max_fixes BETWEEN 100 AND 100000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_retry_interval_h",
                table: "device_config_profiles",
                sql: "moving_retry_interval_h BETWEEN 1 AND 720");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_moving_retry_max_age_h",
                table: "device_config_profiles",
                sql: "moving_retry_max_age_h BETWEEN 0 AND 8760");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_motion_speed_kmph",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_motion_stop_wait_s",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_motion_threshold_mg",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_motion_wake_wait_s",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_config_check_s",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_fix_timeout_s",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_interval_s",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_queue_max_fixes",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_retry_interval_h",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_retry_max_age_h",
                table: "device_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_motion_speed_kmph",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_motion_stop_wait_s",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_motion_threshold_mg",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_motion_wake_wait_s",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_config_check_s",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_fix_timeout_s",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_interval_s",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_queue_max_fixes",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_retry_interval_h",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_moving_retry_max_age_h",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "motion_enabled",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "motion_speed_kmph",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "motion_stop_wait_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "motion_threshold_mg",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "motion_wake_wait_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_config_check_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_fix_timeout_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_interval_s",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_queue_max_fixes",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_retry_interval_h",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_retry_max_age_h",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "moving_sleep_between",
                table: "device_config_versions");

            migrationBuilder.DropColumn(
                name: "motion_enabled",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "motion_speed_kmph",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "motion_stop_wait_s",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "motion_threshold_mg",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "motion_wake_wait_s",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_config_check_s",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_fix_timeout_s",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_interval_s",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_queue_max_fixes",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_retry_interval_h",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_retry_max_age_h",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "moving_sleep_between",
                table: "device_config_profiles");
        }
    }
}
