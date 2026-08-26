using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RaiseMaxFixTimeoutToOneHour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_fix_timeout_s",
                table: "device_config_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_fix_timeout_s",
                table: "device_config_versions",
                sql: "fix_timeout_s BETWEEN 15 AND 3600");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_fix_timeout_s",
                table: "device_config_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_fix_timeout_s",
                table: "device_config_versions",
                sql: "fix_timeout_s BETWEEN 15 AND 900");
        }
    }
}
