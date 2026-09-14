using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreShareSecretsInClear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing share link goes, and it has to.
            //
            // The old rows hold a SHA-256 digest and a PBKDF2 hash. The new columns
            // hold the values themselves, and neither can be derived from the other —
            // that was the whole property of the previous design. Carrying the rows
            // across would land them with an empty verifier and an empty code: links
            // that still look live in their creator's list, still count against the
            // per-device ceiling, and can never be opened by anybody.
            //
            // Deleting them is also what was asked for when this change was agreed
            // (2026-09-12). Recipients of the old links have to be sent a fresh link
            // and code either way, because the ones they hold stopped working the
            // moment these columns changed.
            migrationBuilder.Sql("DELETE FROM share_links;");

            migrationBuilder.DropColumn(
                name: "passphrase_hash",
                table: "share_links");

            migrationBuilder.DropColumn(
                name: "verifier_hash",
                table: "share_links");

            migrationBuilder.AddColumn<string>(
                name: "passphrase",
                table: "share_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "verifier",
                table: "share_links",
                type: "character varying(43)",
                maxLength: 43,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "passphrase",
                table: "share_links");

            migrationBuilder.DropColumn(
                name: "verifier",
                table: "share_links");

            migrationBuilder.AddColumn<string>(
                name: "passphrase_hash",
                table: "share_links",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "verifier_hash",
                table: "share_links",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "");
        }
    }
}
