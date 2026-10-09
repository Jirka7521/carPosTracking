using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <summary>
    /// <c>users.maps_consent_version</c> / <c>maps_consent_granted_at</c> — the
    /// account's standing agreement to load the Google map, moved off the browser so
    /// a person who said "always" is asked once rather than once per device.
    ///
    /// Both nullable, and every existing row starts with nulls: the old per-browser
    /// answer is not carried over, because it was given for one browser and cannot
    /// honestly be recorded as an agreement for the whole account.
    /// </summary>
    public partial class AddMapsConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "maps_consent_granted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maps_consent_version",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maps_consent_granted_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "maps_consent_version",
                table: "users");
        }
    }
}
