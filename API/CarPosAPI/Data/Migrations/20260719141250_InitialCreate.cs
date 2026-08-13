using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    public_key_pem = table.Column<string>(type: "text", nullable: true),
                    private_key_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fix_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    speed_kmph = table.Column<double>(type: "double precision", nullable: false),
                    altitude_m = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.id);
                    table.CheckConstraint("ck_positions_altitude_m", "altitude_m >= -500 AND altitude_m <= 10000");
                    table.CheckConstraint("ck_positions_latitude", "latitude >= -90 AND latitude <= 90");
                    table.CheckConstraint("ck_positions_longitude", "longitude >= -180 AND longitude <= 180");
                    table.CheckConstraint("ck_positions_speed_kmph", "speed_kmph >= 0 AND speed_kmph <= 1000");
                    table.ForeignKey(
                        name: "FK_positions_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_devices_device_id",
                table: "devices",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_positions_device_id_fix_time",
                table: "positions",
                columns: new[] { "device_id", "fix_time" },
                unique: true);

            // Hand-written spatial support. The EF model deliberately excludes this
            // column so the app needs no PostGIS/NetTopologySuite dependency: the
            // database derives location from latitude/longitude itself (generated
            // column), so the two can never drift apart. ST_MakePoint takes
            // (x = longitude, y = latitude); the geography cast is IMMUTABLE on
            // PostGIS 3.6, which generated columns require. Down() needs no mirror —
            // dropping the positions table removes the column and index with it.
            migrationBuilder.Sql(
                """
                ALTER TABLE positions
                    ADD COLUMN location geography(Point,4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography) STORED;
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_positions_location ON positions USING GIST (location);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}
