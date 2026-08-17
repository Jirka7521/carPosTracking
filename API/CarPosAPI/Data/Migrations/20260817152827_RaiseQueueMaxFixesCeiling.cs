using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RaiseQueueMaxFixesCeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_queue_max_fixes",
                table: "device_config_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_queue_max_fixes",
                table: "device_config_versions",
                sql: "queue_max_fixes BETWEEN 100 AND 1000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_config_versions_queue_max_fixes",
                table: "device_config_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_config_versions_queue_max_fixes",
                table: "device_config_versions",
                sql: "queue_max_fixes BETWEEN 100 AND 100000");
        }
    }
}
