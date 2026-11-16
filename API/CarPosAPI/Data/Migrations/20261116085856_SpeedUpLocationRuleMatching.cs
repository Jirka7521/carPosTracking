using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <summary>
    /// Makes place-rule matching cost per trip instead of per trip × rule. No data or
    /// results change — only how the database finds them.
    ///
    /// <para>
    /// The first version (<c>AddJourneyLocationRules</c>) tested every rule with
    /// <c>trip_near(device, start, end, center, radius)</c>, which probed the trip's start,
    /// parking and end fixes again for each rule. Here the fixes are looked up once per
    /// trip (<c>trip_points</c>), and each rule only compares numbers and, when the
    /// latitude is close enough, runs <c>ST_DWithin</c>. Measured on a device with 224
    /// trips: journey counts with 10 rules 138 → 31 ms, with 100 rules (the cap)
    /// 1 255 → 74 ms; a 51-trip page with 100 rules 288 → 21 ms; the preview's points
    /// 734 → 26 ms (it no longer hash-joins all of the device's fixes, and drops below
    /// the JIT threshold).
    /// </para>
    /// </summary>
    public partial class SpeedUpLocationRuleMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Where a trip starts, where the car stood before it, and where it ends — the one
            // definition, used by the view and the location-rule preview. One row; a column is
            // NULL when there is no such fix.
            //   start: the fix at start_time;
            //   park:  the last fix before it, when that fix lies after the previous trip's
            //          end (a fix of the stop the car left from, not the end of a hard gap).
            //          After a sleep the first moving fix can already be hundreds of metres
            //          from where the car stood; the parking fix is where it actually stood;
            //   end:   the fix at end_time (the first fix of the stop that ended it, or the
            //          last fix before a hard gap); none while the end is undecided.
            // Each is one index probe on (device_id, fix_time) or (device_id, start_time).
            // OFFSET 0 keeps the planner from copying the lookups into every place a column
            // is used, so they run once per trip, not once per rule.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION trip_points(
                    p_device uuid,
                    p_start timestamptz,
                    p_end timestamptz)
                RETURNS TABLE (
                    start_location geography,
                    start_latitude double precision,
                    park_location geography,
                    park_latitude double precision,
                    end_location geography,
                    end_latitude double precision)
                LANGUAGE sql STABLE PARALLEL SAFE
                AS $$
                    SELECT s.location, s.latitude, k.location, k.latitude, e.location, e.latitude
                    FROM (SELECT 1) one
                    LEFT JOIN positions s
                           ON s.device_id = p_device
                          AND s.fix_time = p_start
                    LEFT JOIN LATERAL (
                        SELECT park.location, park.latitude
                        FROM (SELECT pp.fix_time, pp.location, pp.latitude
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
                    ) k ON true
                    LEFT JOIN positions e
                           ON e.device_id = p_device
                          AND e.fix_time = p_end
                    OFFSET 0
                $$;
                """);

            // Whether a trip's points (from trip_points) lie within p_radius metres of
            // p_center — the one definition of "starts here" / "ends here":
            //   at_start:    the start fix or the parking fix is inside;
            //   at_end:      the end fix is inside;
            //   start_match: the point that made at_start true (the start fix first).
            // ST_DWithin (on the spheroid) decides. The latitude test in front only skips
            // it when it cannot be true: a degree of latitude is at least 110 574 m long
            // anywhere on WGS84, so a point within p_radius metres differs in latitude by
            // at most p_radius / 110 574 degrees — 110 000 leaves a margin. No wrap-around
            // to handle, unlike longitude. Plain SQL and not STRICT, so it is inlined.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION trip_near(
                    p_start_location geography,
                    p_start_latitude double precision,
                    p_park_location geography,
                    p_park_latitude double precision,
                    p_end_location geography,
                    p_end_latitude double precision,
                    p_center geography,
                    p_center_latitude double precision,
                    p_radius double precision)
                RETURNS TABLE (at_start boolean, at_end boolean, start_match geography)
                LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$
                    SELECT
                        m.start_match IS NOT NULL,
                        COALESCE(abs(p_end_latitude - p_center_latitude) <= p_radius / 110000.0
                                 AND ST_DWithin(p_end_location, p_center, p_radius), false),
                        m.start_match
                    FROM (SELECT CASE
                                     WHEN abs(p_start_latitude - p_center_latitude) <= p_radius / 110000.0
                                          AND ST_DWithin(p_start_location, p_center, p_radius)
                                         THEN p_start_location
                                     WHEN abs(p_park_latitude - p_center_latitude) <= p_radius / 110000.0
                                          AND ST_DWithin(p_park_location, p_center, p_radius)
                                         THEN p_park_location
                                 END AS start_match) m
                $$;
                """);

            // Same columns, same rule, same precedence as before (see AddJourneyLocationRules):
            //   1. pinned by hand: the journey stored on the trip (NULL = "in none");
            //   2. a location rule in force at the trip's start ([valid_from, ended_at))
            //      whose circle holds its start or end; among several, an end match beats a
            //      start match, then the smaller circle, then the newer rule;
            //   3. the innermost journey whose [range_from, range_to) holds the start;
            //   4. none.
            // What changed: the trip's points are looked up once (trip_points, in the rule
            // lateral's outer side) and only when a rule is in force at the trip's start —
            // the EXISTS gate, so trips before a rule's valid_from, pinned trips and devices
            // without rules cost nothing extra.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW trip_effective_journeys AS
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
                    FROM trip_points(t.device_id, t.start_time, t.end_time) p
                    JOIN journey_location_rules r
                      ON r.device_id = t.device_id
                     AND r.journey_id IS NOT NULL
                     AND r.valid_from <= t.start_time
                     AND (r.ended_at IS NULL OR t.start_time < r.ended_at)
                    CROSS JOIN LATERAL trip_near(
                        p.start_location, p.start_latitude,
                        p.park_location, p.park_latitude,
                        p.end_location, p.end_latitude,
                        r.center, r.latitude, r.radius_m) n
                    WHERE NOT t.journey_pinned
                      AND EXISTS (
                          SELECT 1
                          FROM journey_location_rules g
                          WHERE g.device_id = t.device_id
                            AND g.journey_id IS NOT NULL
                            AND g.valid_from <= t.start_time
                            AND (g.ended_at IS NULL OR t.start_time < g.ended_at))
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

            migrationBuilder.Sql("DROP FUNCTION trip_near(uuid, timestamptz, timestamptz, geography, double precision);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to AddJourneyLocationRules' definitions, verbatim.
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

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW trip_effective_journeys AS
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

            migrationBuilder.Sql(
                """
                DROP FUNCTION trip_near(
                    geography, double precision,
                    geography, double precision,
                    geography, double precision,
                    geography, double precision,
                    double precision);
                """);
            migrationBuilder.Sql("DROP FUNCTION trip_points(uuid, timestamptz, timestamptz);");
        }
    }
}
