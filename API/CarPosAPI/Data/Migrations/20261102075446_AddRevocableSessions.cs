using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRevocableSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Every existing account gets its own stamp. Tokens issued before this
            // migration carry none and are refused anyway (SessionClaims.TryRead), so
            // the value only has to be unique per user, not match anything.
            migrationBuilder.Sql(
                "UPDATE users SET security_stamp = md5(random()::text || clock_timestamp()::text || id::text);");

            migrationBuilder.CreateTable(
                name: "revoked_sessions",
                columns: table => new
                {
                    token_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revoked_sessions", x => x.token_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revoked_sessions_expires_at",
                table: "revoked_sessions",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revoked_sessions");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");
        }
    }
}
