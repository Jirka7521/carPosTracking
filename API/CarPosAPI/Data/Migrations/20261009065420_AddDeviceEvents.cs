using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each device's connection history - when it went offline and why (a planned
            // sleep, the power switch, a flat battery, a caught fault, or the Last Will the
            // broker publishes when a session dies), plus the restarts worth knowing about -
            // and devices.last_online_at, the time it last announced a broker connection.
            // Purely additive: no existing row or column changes.
            //
            // Apply it BEFORE deploying the API that reads these: /api/me/devices selects
            // last_online_at and the latest device_events row for every card, and would
            // fail outright against a database that has neither.
            migrationBuilder.AddColumn<DateTime>(
                name: "last_online_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    device_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    battery_pct = table.Column<int>(type: "integer", nullable: true),
                    sleep_seconds = table.Column<int>(type: "integer", nullable: true),
                    detail = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_events", x => x.id);
                    table.CheckConstraint("ck_device_events_battery_pct", "battery_pct >= 0 AND battery_pct <= 100");
                    table.CheckConstraint("ck_device_events_kind", "kind IN ('offline', 'restart')");
                    table.CheckConstraint("ck_device_events_reason", "(kind = 'offline' AND reason IN ('sleep', 'powerOff', 'batteryLow', 'error', 'connectionLost')) OR (kind = 'restart' AND reason IN ('powerOn', 'powerLoss', 'crash'))");
                    table.CheckConstraint("ck_device_events_severity", "severity IN ('normal', 'alert', 'error')");
                    table.CheckConstraint("ck_device_events_sleep_seconds", "sleep_seconds >= 0 AND sleep_seconds <= 86400");
                    table.ForeignKey(
                        name: "FK_device_events_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_events_device_id_received_at",
                table: "device_events",
                columns: new[] { "device_id", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_events");

            migrationBuilder.DropColumn(
                name: "last_online_at",
                table: "devices");
        }
    }
}
