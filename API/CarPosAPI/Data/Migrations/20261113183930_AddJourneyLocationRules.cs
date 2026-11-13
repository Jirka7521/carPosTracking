using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJourneyLocationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "end_time",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "journey_location_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    radius_m = table.Column<int>(type: "integer", nullable: false),
                    match_start = table.Column<bool>(type: "boolean", nullable: false),
                    match_end = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_location_rules", x => x.id);
                    table.CheckConstraint("ck_journey_location_rules_end", "(ended_at IS NULL AND end_reason IS NULL) OR (ended_at IS NOT NULL AND end_reason IN ('user', 'journeyDeleted'))");
                    table.CheckConstraint("ck_journey_location_rules_latitude", "latitude >= -90 AND latitude <= 90");
                    table.CheckConstraint("ck_journey_location_rules_longitude", "longitude >= -180 AND longitude <= 180");
                    table.CheckConstraint("ck_journey_location_rules_match", "match_start OR match_end");
                    table.CheckConstraint("ck_journey_location_rules_name_not_blank", "name IS NULL OR length(btrim(name)) > 0");
                    table.CheckConstraint("ck_journey_location_rules_radius_m", "radius_m BETWEEN 50 AND 10000");
                    table.CheckConstraint("ck_journey_location_rules_target", "journey_id IS NOT NULL OR ended_at IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_journey_location_rules_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_journey_location_rules_journeys_journey_id",
                        column: x => x.journey_id,
                        principalTable: "journeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_trips_end_after_start",
                table: "trips",
                sql: "end_time IS NULL OR end_time >= start_time");

            migrationBuilder.CreateIndex(
                name: "ix_journey_location_rules_device_id_valid_from",
                table: "journey_location_rules",
                columns: new[] { "device_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_journey_location_rules_journey_id",
                table: "journey_location_rules",
                column: "journey_id",
                filter: "journey_id IS NOT NULL");

            // Hand-written spatial support, the same arrangement as positions.location in
            // InitialCreate: the rule's centre as a generated geography column, derived by
            // the database from latitude/longitude, absent from the EF model so the app
            // needs no spatial dependency. No GIST index: a device has at most a few
            // hundred rules, always reached by device_id first.
            migrationBuilder.Sql(
                """
                ALTER TABLE journey_location_rules
                    ADD COLUMN center geography(Point,4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography) STORED;
                """);

            // The one definition of "this trip starts / ends within p_radius metres of
            // p_center", shared by the view below and the location-rule preview.
            //   start: the trip's first fix, or the parking fix — the last fix before it,
            //          when that fix lies after the previous trip's end (a fix of the stop
            //          the car left from, not the end of a hard gap). After a sleep the
            //          first moving fix can already be hundreds of metres from where the
            //          car stood; the parking fix is where it actually stood.
            //   end:   the fix at end_time (the first fix of the stop that ended it, or the
            //          last fix before a hard gap); never while the end is undecided.
            // Every lookup is an index probe on (device_id, fix_time) or
            // (device_id, start_time). Plain SQL, STABLE and not STRICT, so the planner
            // inlines it into the caller.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION trip_near(
                    p_device uuid,
                    p_start timestamptz,
                    p_end timestamptz,
                    p_center geography,
                    p_radius double precision)
                RETURNS TABLE (at_start boolean, at_end boolean)
                LANGUAGE sql STABLE PARALLEL SAFE
                AS $$
                    SELECT
                        EXISTS (
                            SELECT 1
                            FROM positions s
                            WHERE s.device_id = p_device
                              AND s.fix_time = p_start
                              AND ST_DWithin(s.location, p_center, p_radius))
                        OR EXISTS (
                            SELECT 1
                            FROM (SELECT pp.fix_time, pp.location
                                  FROM positions pp
                                  WHERE pp.device_id = p_device
                                    AND pp.fix_time < p_start
                                  ORDER BY pp.fix_time DESC
                                  LIMIT 1) park
                            WHERE park.fix_time > COALESCE(
                                      (SELECT o.end_time
                                       FROM trips o
                                       WHERE o.device_id = p_device
                                         AND o.start_time < p_start
                                       ORDER BY o.start_time DESC
                                       LIMIT 1),
                                      '-infinity'::timestamptz)
                              AND ST_DWithin(park.location, p_center, p_radius)),
                        p_end IS NOT NULL AND EXISTS (
                            SELECT 1
                            FROM positions e
                            WHERE e.device_id = p_device
                              AND e.fix_time = p_end
                              AND ST_DWithin(e.location, p_center, p_radius))
                $$;
                """);

            // Which journey each trip is in right now — the whole rule, in one place, and
            // never written back. In order of precedence:
            //   1. pinned by hand: the journey stored on the trip (NULL = "in none");
            //   2. a location rule in force at the trip's start ([valid_from, ended_at))
            //      whose circle holds its start or end; among several, an end match beats a
            //      start match, then the smaller circle, then the newer rule;
            //   3. the innermost journey whose [range_from, range_to) holds the start
            //      (ranges are disjoint or nested, so that is the latest-starting one);
            //   4. none.
            // A rule whose journey was deleted at the top level (journey_id NULL) places
            // nothing. Mapped read-only by TripEffectiveJourneyConfiguration.
            migrationBuilder.Sql(
                """
                CREATE VIEW trip_effective_journeys AS
                SELECT
                    t.id AS trip_id,
                    t.device_id,
                    t.start_time,
                    t.end_time,
                    t.journey_pinned,
                    t.name,
                    (SELECT min(o.start_time)
                     FROM trips o
                     WHERE o.device_id = t.device_id
                       AND o.start_time > t.start_time) AS next_start,
                    CASE
                        WHEN t.journey_pinned THEN t.journey_id
                        ELSE COALESCE(lr.journey_id, dr.journey_id)
                    END AS effective_journey_id,
                    CASE
                        WHEN t.journey_pinned THEN 'manual'
                        WHEN lr.journey_id IS NOT NULL THEN 'locationRule'
                        WHEN dr.journey_id IS NOT NULL THEN 'dateRange'
                    END AS journey_source,
                    lr.rule_id AS location_rule_id,
                    lr.at_end AS location_rule_at_end
                FROM trips t
                LEFT JOIN LATERAL (
                    SELECT r.id AS rule_id, r.journey_id, (r.match_end AND n.at_end) AS at_end
                    FROM journey_location_rules r
                    CROSS JOIN LATERAL trip_near(t.device_id, t.start_time, t.end_time, r.center, r.radius_m) n
                    WHERE NOT t.journey_pinned
                      AND r.device_id = t.device_id
                      AND r.journey_id IS NOT NULL
                      AND r.valid_from <= t.start_time
                      AND (r.ended_at IS NULL OR t.start_time < r.ended_at)
                      AND ((r.match_start AND n.at_start) OR (r.match_end AND n.at_end))
                    ORDER BY (r.match_end AND n.at_end) DESC, r.radius_m, r.created_at DESC, r.id
                    LIMIT 1
                ) lr ON true
                LEFT JOIN LATERAL (
                    SELECT j.id AS journey_id
                    FROM journeys j
                    WHERE NOT t.journey_pinned
                      AND j.device_id = t.device_id
                      AND j.range_from <= t.start_time
                      AND t.start_time < j.range_to
                    ORDER BY j.range_from DESC, j.range_to
                    LIMIT 1
                ) dr ON true;
                """);

            // Existing trips have no end yet. Mark every device's whole history dirty (and
            // bump the epoch, discarding any in-flight result): the worker's startup
            // catch-up pass recomputes it in place, keeping ids, names and placements, and
            // fills end_time in. The dashboard shows "Recalculating trips…" meanwhile.
            migrationBuilder.Sql(
                "UPDATE trip_detection_state SET dirty_from = '1970-01-01T00:00:00Z', epoch = epoch + 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW trip_effective_journeys;");
            migrationBuilder.Sql("DROP FUNCTION trip_near(uuid, timestamptz, timestamptz, geography, double precision);");

            migrationBuilder.DropTable(
                name: "journey_location_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trips_end_after_start",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "trips");
        }
    }
}
