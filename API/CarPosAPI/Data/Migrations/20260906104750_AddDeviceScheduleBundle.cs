using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceScheduleBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "reported_profile_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reported_profile_slot",
                table: "devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reported_schedule_version",
                table: "devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schedule_bundle_version",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "schedule_slot",
                table: "device_config_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Hand-written, and it must run before the unique index below: the column
            // default gives every existing profile slot 0, so any device with two or
            // more profiles would fail to index. Numbering is dense from zero per
            // device, ordered by creation, which matches how LowestFreeSlot hands out
            // slots from here on.
            //
            // No device can have more than ScheduleRules.MaxProfilesPerDevice profiles —
            // that cap has been enforced since profiles existed — so the check
            // constraint added afterwards cannot be violated by this backfill. If it
            // somehow is, the migration failing loudly is the correct outcome: a slot
            // outside the range is one the firmware could not address.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT id,
                           row_number() OVER (PARTITION BY device_id ORDER BY created_at, id) - 1 AS slot
                    FROM device_config_profiles
                )
                UPDATE device_config_profiles AS p
                SET schedule_slot = ranked.slot
                FROM ranked
                WHERE p.id = ranked.id
                """);

            migrationBuilder.CreateIndex(
                name: "ux_device_config_profiles_device_id_schedule_slot",
                table: "device_config_profiles",
                columns: new[] { "device_id", "schedule_slot" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_profiles_schedule_slot",
                table: "device_config_profiles",
                sql: "schedule_slot BETWEEN 0 AND 11");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_device_config_profiles_device_id_schedule_slot",
                table: "device_config_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_profiles_schedule_slot",
                table: "device_config_profiles");

            migrationBuilder.DropColumn(
                name: "reported_profile_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "reported_profile_slot",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "reported_schedule_version",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "schedule_bundle_version",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "schedule_slot",
                table: "device_config_profiles");
        }
    }
}
