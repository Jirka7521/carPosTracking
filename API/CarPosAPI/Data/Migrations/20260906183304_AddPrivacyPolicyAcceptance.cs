using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <summary>
    /// The schema half of the GDPR work. Two changes that travel together because
    /// they are both about what happens to a person's data:
    ///
    /// <list type="bullet">
    /// <item><c>users.privacy_policy_version</c> / <c>privacy_policy_accepted_at</c> —
    /// the record that consent was actually given, and to which text. Art. 7(1) puts
    /// the burden of demonstrating that on the controller.</item>
    /// <item><c>accesses.granted_by</c> becomes nullable — the thing that makes
    /// account erasure possible at all. Every FK pointing at <c>users</c> is
    /// Restrict, so a grant this user handed to somebody else would pin their row in
    /// the table forever. Nulling the reference keeps the other person's access
    /// working while removing the link to the erased account.</item>
    /// </list>
    ///
    /// Existing rows take the defaults: an empty version string and a null acceptance
    /// time, which is the honest record for an account created before the policy
    /// existed. Nothing invents a consent that was never given.
    /// </summary>
    public partial class AddPrivacyPolicyAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "privacy_policy_accepted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "privacy_policy_version",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "granted_by",
                table: "accesses",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "privacy_policy_accepted_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "privacy_policy_version",
                table: "users");

            migrationBuilder.AlterColumn<int>(
                name: "granted_by",
                table: "accesses",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
