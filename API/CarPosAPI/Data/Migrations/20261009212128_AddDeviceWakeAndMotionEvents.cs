using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceWakeAndMotionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The device history grows from "offline and notable restarts" to every wake
            // from deep sleep, every motion-wake step and a separate "sleep because parked"
            // reason - the kind/reason CHECKs widen to admit them. And because those events
            // can now wait on the device's SD card and arrive hours late, each row gets
            // occurred_at, the time it HAPPENED, which the history is ordered and filtered
            // by; the index moves with it.
            //
            // Every existing row was a live message, so its occurred_at is its received_at:
            // added nullable, backfilled, then made required, rather than given a default
            // that would date the whole history to year 1.
            //
            // Apply it BEFORE deploying the API that writes these: the old CHECKs reject the
            // new kinds and every insert would fail without occurred_at.
            migrationBuilder.DropIndex(
                name: "ix_device_events_device_id_received_at",
                table: "device_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_events_kind",
                table: "device_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_events_reason",
                table: "device_events");

            migrationBuilder.AddColumn<DateTime>(
                name: "occurred_at",
                table: "device_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE device_events SET occurred_at = received_at;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at",
                table: "device_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_events_device_id_occurred_at",
                table: "device_events",
                columns: new[] { "device_id", "occurred_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_events_kind",
                table: "device_events",
                sql: "kind IN ('offline', 'restart', 'wake', 'motion')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_events_reason",
                table: "device_events",
                sql: "(kind = 'offline' AND reason IN ('sleep', 'sleepNoMotion', 'powerOff', 'batteryLow', 'error', 'connectionLost')) OR (kind = 'restart' AND reason IN ('powerOn', 'powerLoss', 'crash')) OR (kind = 'wake' AND reason IN ('timer', 'accelerometer', 'powerSwitch')) OR (kind = 'motion' AND reason IN ('checking', 'activity', 'motionOn', 'moving', 'noMotion', 'stopped', 'motionOff'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old CHECKs cannot hold the new rows, so they are fitted first: a parked
            // sleep is still a sleep, while wakes and motion steps are history the old
            // schema has no words for and go.
            migrationBuilder.Sql(
                "UPDATE device_events SET reason = 'sleep' WHERE reason = 'sleepNoMotion';");
            migrationBuilder.Sql("DELETE FROM device_events WHERE kind IN ('wake', 'motion');");

            migrationBuilder.DropIndex(
                name: "ix_device_events_device_id_occurred_at",
                table: "device_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_events_kind",
                table: "device_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_device_events_reason",
                table: "device_events");

            migrationBuilder.DropColumn(
                name: "occurred_at",
                table: "device_events");

            migrationBuilder.CreateIndex(
                name: "ix_device_events_device_id_received_at",
                table: "device_events",
                columns: new[] { "device_id", "received_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_events_kind",
                table: "device_events",
                sql: "kind IN ('offline', 'restart')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_events_reason",
                table: "device_events",
                sql: "(kind = 'offline' AND reason IN ('sleep', 'powerOff', 'batteryLow', 'error', 'connectionLost')) OR (kind = 'restart' AND reason IN ('powerOn', 'powerLoss', 'crash'))");
        }
    }
}
