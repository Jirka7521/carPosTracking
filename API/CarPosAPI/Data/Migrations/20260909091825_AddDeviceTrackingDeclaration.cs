using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <summary>
    /// <c>devices.tracking_declaration_accepted_at</c> — when the person registering
    /// a device confirmed they were entitled to track the vehicle and would tell the
    /// people who drive it.
    ///
    /// A tracker usually ends up in a car somebody else also drives, and that person
    /// is a data subject who never signed up here. The terms of use make informing
    /// them the account holder&apos;s duty; this column is the evidence that the duty
    /// was accepted, per device rather than once at registration, because it is a
    /// statement about a particular vehicle.
    ///
    /// Nullable, and existing rows keep null: devices registered before the
    /// declaration existed were never declared for, and inventing a timestamp for
    /// them would forge exactly the record this column exists to hold.
    /// </summary>
    public partial class AddDeviceTrackingDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "tracking_declaration_accepted_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tracking_declaration_accepted_at",
                table: "devices");
        }
    }
}
