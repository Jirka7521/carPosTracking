using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    selector = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    verifier_hash = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    passphrase_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    include_speed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    include_telemetry = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    successful_redeems = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_accessed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_share_links_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_share_links_device_id",
                table: "share_links",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ux_share_links_selector",
                table: "share_links",
                column: "selector",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "share_links");
        }
    }
}
