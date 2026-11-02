using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_owner",
                table: "accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every existing device gets an owner: its earliest active grant, and among
            // those the self-granted one — the grant registration writes for the
            // creator (granted_by = user_id) — whenever it is still active. Done before
            // the unique index exists, and DISTINCT ON picks exactly one per device.
            migrationBuilder.Sql(@"
UPDATE accesses AS a
SET is_owner = TRUE
FROM (
    SELECT DISTINCT ON (device_id) id
    FROM accesses
    WHERE is_active
    ORDER BY device_id, (granted_by = user_id) DESC NULLS LAST, date_registration, id
) AS first_grant
WHERE a.id = first_grant.id;");

            migrationBuilder.CreateIndex(
                name: "ux_accesses_device_owner",
                table: "accesses",
                column: "device_id",
                unique: true,
                filter: "is_owner AND is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_accesses_device_owner",
                table: "accesses");

            migrationBuilder.DropColumn(
                name: "is_owner",
                table: "accesses");
        }
    }
}
