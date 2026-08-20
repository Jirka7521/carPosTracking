using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RaiseDefaultConfigCheckInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "config_check_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 3600,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 900);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "config_check_s",
                table: "device_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 900,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3600);
        }
    }
}
