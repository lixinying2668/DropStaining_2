using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterSupplyMock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "water_supply_channel_states",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    channel_no = table.Column<int>(type: "INTEGER", nullable: false),
                    channel_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    inlet_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_target_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_volume_ml = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_flow_rate_ml_per_minute = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    is_connected = table.Column<bool>(type: "INTEGER", nullable: false),
                    current_command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    fault_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    fault_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_water_supply_channel_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "water_supply_telemetry",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    source_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    channel_no = table.Column<int>(type: "INTEGER", nullable: false),
                    channel_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    inlet_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_target_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_volume_ml = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_flow_rate_ml_per_minute = table.Column<int>(type: "INTEGER", nullable: false),
                    outlet_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    is_connected = table.Column<bool>(type: "INTEGER", nullable: false),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    fault_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_water_supply_telemetry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_water_supply_channel_states_channel_code",
                table: "water_supply_channel_states",
                column: "channel_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_water_supply_channel_states_channel_no",
                table: "water_supply_channel_states",
                column: "channel_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_water_supply_telemetry_recorded_at_utc",
                table: "water_supply_telemetry",
                column: "recorded_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_water_supply_telemetry_source_id_recorded_at_utc",
                table: "water_supply_telemetry",
                columns: new[] { "source_id", "recorded_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "water_supply_channel_states");

            migrationBuilder.DropTable(
                name: "water_supply_telemetry");
        }
    }
}
