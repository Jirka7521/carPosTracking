using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class HashShareSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hashed in place, so every live link and code keeps working: the columns
            // are renamed and widened to hold a SHA-256 hex digest, then each value is
            // replaced by the digest ShareSecretHash computes — the verifier as is,
            // the code in PassphraseGenerator.Normalise's form (upper case, only the
            // generator's alphabet). ShareSecretHashTests pins both equivalences.
            migrationBuilder.RenameColumn(
                name: "verifier",
                table: "share_links",
                newName: "verifier_hash");

            migrationBuilder.RenameColumn(
                name: "passphrase",
                table: "share_links",
                newName: "passphrase_hash");

            migrationBuilder.AlterColumn<string>(
                name: "verifier_hash",
                table: "share_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(43)",
                oldMaxLength: 43);

            migrationBuilder.AlterColumn<string>(
                name: "passphrase_hash",
                table: "share_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.Sql(@"
UPDATE share_links
SET verifier_hash = encode(sha256(convert_to(verifier_hash, 'UTF8')), 'hex'),
    passphrase_hash = encode(sha256(convert_to(
        regexp_replace(upper(passphrase_hash), '[^ABCDEFGHJKMNPQRSTUVWXYZ2-9]', '', 'g'), 'UTF8')), 'hex');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A digest cannot be turned back into the secret. Going down deletes the
            // links (the same choice StoreShareSecretsInClear made going the other way)
            // and restores the columns empty.
            migrationBuilder.Sql("DELETE FROM share_links;");

            migrationBuilder.RenameColumn(
                name: "verifier_hash",
                table: "share_links",
                newName: "verifier");

            migrationBuilder.RenameColumn(
                name: "passphrase_hash",
                table: "share_links",
                newName: "passphrase");

            migrationBuilder.AlterColumn<string>(
                name: "verifier",
                table: "share_links",
                type: "character varying(43)",
                maxLength: 43,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
