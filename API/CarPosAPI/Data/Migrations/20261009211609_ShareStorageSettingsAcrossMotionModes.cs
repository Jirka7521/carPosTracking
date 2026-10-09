using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShareStorageSettingsAcrossMotionModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_moving_config_check_s",
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
                name: "ck_device_config_profiles_moving_config_check_s",
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
                name: "moving_config_check_s",
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
                name: "moving_config_check_s",
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

            migrationBuilder.AlterColumn<int>(
                name: "moving_interval_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 10);

            migrationBuilder.AlterColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 600,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 240);

            migrationBuilder.AlterColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 188,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 63);

            migrationBuilder.AlterColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 900,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 600);

            migrationBuilder.AlterColumn<bool>(
                name: "motion_enabled",
                table: "device_config_versions",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "moving_interval_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 10);

            migrationBuilder.AlterColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 600,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 240);

            migrationBuilder.AlterColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 188,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 63);

            migrationBuilder.AlterColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 900,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 600);

            migrationBuilder.AlterColumn<bool>(
                name: "motion_enabled",
                table: "device_config_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "moving_interval_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 10,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 30);

            migrationBuilder.AlterColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 240,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 600);

            migrationBuilder.AlterColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 63,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 188);

            migrationBuilder.AlterColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 600,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 900);

            migrationBuilder.AlterColumn<bool>(
                name: "motion_enabled",
                table: "device_config_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "moving_config_check_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 3600);

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

            migrationBuilder.AlterColumn<int>(
                name: "moving_interval_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 10,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 30);

            migrationBuilder.AlterColumn<int>(
                name: "motion_wake_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 240,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 600);

            migrationBuilder.AlterColumn<int>(
                name: "motion_threshold_mg",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 63,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 188);

            migrationBuilder.AlterColumn<int>(
                name: "motion_stop_wait_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 600,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 900);

            migrationBuilder.AlterColumn<bool>(
                name: "motion_enabled",
                table: "device_config_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "moving_config_check_s",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 3600);

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_moving_config_check_s",
                table: "device_config_versions",
                sql: "moving_config_check_s BETWEEN 60 AND 86400");

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
                name: "ck_device_config_profiles_moving_config_check_s",
                table: "device_config_profiles",
                sql: "moving_config_check_s BETWEEN 60 AND 86400");

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
    }
}
