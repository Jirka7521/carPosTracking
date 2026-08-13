using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarPosAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatteryAndAccel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "accel_x_g",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "accel_y_g",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "accel_z_g",
                table: "positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "battery_pct",
                table: "positions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_accel_x_g",
                table: "positions",
                sql: "accel_x_g >= -16 AND accel_x_g <= 16");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_accel_y_g",
                table: "positions",
                sql: "accel_y_g >= -16 AND accel_y_g <= 16");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_accel_z_g",
                table: "positions",
                sql: "accel_z_g >= -16 AND accel_z_g <= 16");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_battery_pct",
                table: "positions",
                sql: "battery_pct >= 0 AND battery_pct <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_accel_x_g",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_accel_y_g",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_accel_z_g",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_battery_pct",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "accel_x_g",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "accel_y_g",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "accel_z_g",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "battery_pct",
                table: "positions");
        }
    }
}
