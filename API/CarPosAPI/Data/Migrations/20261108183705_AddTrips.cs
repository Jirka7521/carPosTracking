using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_trip_settings",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_stop_seconds = table.Column<int>(type: "integer", nullable: false),
                    stop_radius_m = table.Column<int>(type: "integer", nullable: false),
                    min_trip_distance_m = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_trip_settings", x => x.device_id);
                    table.CheckConstraint("ck_device_trip_settings_min_stop_seconds", "min_stop_seconds BETWEEN 60 AND 14400");
                    table.CheckConstraint("ck_device_trip_settings_min_trip_distance_m", "min_trip_distance_m BETWEEN 0 AND 20000");
                    table.CheckConstraint("ck_device_trip_settings_stop_radius_m", "stop_radius_m BETWEEN 20 AND 1000");
                    table.ForeignKey(
                        name: "FK_device_trip_settings_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "journeys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    range_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    range_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journeys", x => x.id);
                    table.CheckConstraint("ck_journeys_name_not_blank", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_journeys_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                    table.CheckConstraint("ck_journeys_range", "(range_from IS NULL AND range_to IS NULL) OR (range_from IS NOT NULL AND range_to IS NOT NULL AND range_from < range_to)");
                    table.ForeignKey(
                        name: "FK_journeys_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_journeys_journeys_parent_id",
                        column: x => x.parent_id,
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_detection_cursor",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    last_position_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_detection_cursor", x => x.id);
                    table.CheckConstraint("ck_trip_detection_cursor_last_position_id", "last_position_id >= 0");
                    table.CheckConstraint("ck_trip_detection_cursor_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "trip_detection_state",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dirty_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    epoch = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_pass_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_detection_state", x => x.device_id);
                    table.CheckConstraint("ck_trip_detection_state_epoch", "epoch >= 0");
                    table.ForeignKey(
                        name: "FK_trip_detection_state_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journey_pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trips", x => x.id);
                    table.CheckConstraint("ck_trips_journey_pinned", "journey_pinned OR journey_id IS NULL");
                    table.CheckConstraint("ck_trips_name_not_blank", "name IS NULL OR length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "FK_trips_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trips_journeys_journey_id",
                        column: x => x.journey_id,
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "trip_detection_cursor",
                columns: new[] { "id", "last_position_id" },
                values: new object[] { (short)1, 0L });

            migrationBuilder.CreateIndex(
                name: "ix_journeys_device_id_range_from",
                table: "journeys",
                columns: new[] { "device_id", "range_from" },
                filter: "range_from IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_journeys_parent_id",
                table: "journeys",
                column: "parent_id",
                filter: "parent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_journeys_device_id_parent_id_name",
                table: "journeys",
                columns: new[] { "device_id", "parent_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_trips_device_id_start_time",
                table: "trips",
                columns: new[] { "device_id", "start_time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_trips_journey_id",
                table: "trips",
                column: "journey_id",
                filter: "journey_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_trip_settings");

            migrationBuilder.DropTable(
                name: "trip_detection_cursor");

            migrationBuilder.DropTable(
                name: "trip_detection_state");

            migrationBuilder.DropTable(
                name: "trips");

            migrationBuilder.DropTable(
                name: "journeys");
        }
    }
}
