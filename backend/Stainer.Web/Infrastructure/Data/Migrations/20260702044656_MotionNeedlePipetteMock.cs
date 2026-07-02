using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MotionNeedlePipetteMock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dispense_executions_device_command_execution_id",
                table: "dispense_executions");

            migrationBuilder.CreateTable(
                name: "machine_resource_leases",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    resource_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    resource_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    device_command_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    command_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    wait_reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    acquired_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    released_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_resource_leases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "needle_states",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    needle_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    needle_no = table.Column<int>(type: "INTEGER", nullable: false),
                    is_connected = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    loaded_source_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    loaded_reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    source_bottle_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    dab_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    system_liquid_source_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    source_position_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    liquid_class_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    liquid_class_version_no = table.Column<int>(type: "INTEGER", nullable: true),
                    liquid_class_parameters_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false, defaultValue: "{}"),
                    needs_wash = table.Column<bool>(type: "INTEGER", nullable: false),
                    current_command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    device_command_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    last_error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    last_error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_needle_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipetting_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    operation_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    needle_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    execution_mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    target_point_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    secondary_target_point_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    coordinate_profile_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    liquid_class_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    liquid_class_version_no = table.Column<int>(type: "INTEGER", nullable: true),
                    liquid_class_parameters_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false, defaultValue: "{}"),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    reagent_bottle_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    dab_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    system_liquid_source_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    source_position_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    device_command_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipetting_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "robot_arm_states",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    is_homed = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_connected = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    current_target_point_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    current_x_um = table.Column<long>(type: "INTEGER", nullable: true),
                    current_y_um = table.Column<long>(type: "INTEGER", nullable: true),
                    current_z_um = table.Column<long>(type: "INTEGER", nullable: true),
                    coordinate_profile_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    current_command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    device_command_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    last_error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    last_error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_robot_arm_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reagent_consumptions_device_command_execution_id_reagent_bottle_id",
                table: "reagent_consumptions",
                columns: new[] { "device_command_execution_id", "reagent_bottle_id" },
                unique: true,
                filter: "device_command_execution_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_dispense_executions_device_command_execution_id_reagent_bottle_id",
                table: "dispense_executions",
                columns: new[] { "device_command_execution_id", "reagent_bottle_id" },
                unique: true,
                filter: "reagent_bottle_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_machine_resource_leases_device_command_execution_id",
                table: "machine_resource_leases",
                column: "device_command_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_machine_resource_leases_machine_run_id_workflow_step_execution_id",
                table: "machine_resource_leases",
                columns: new[] { "machine_run_id", "workflow_step_execution_id" });

            migrationBuilder.CreateIndex(
                name: "IX_machine_resource_leases_resource_code",
                table: "machine_resource_leases",
                column: "resource_code",
                unique: true,
                filter: "status = 'Acquired'");

            migrationBuilder.CreateIndex(
                name: "IX_needle_states_needle_code",
                table: "needle_states",
                column: "needle_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_needle_states_needle_no",
                table: "needle_states",
                column: "needle_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipetting_operations_device_command_execution_id",
                table: "pipetting_operations",
                column: "device_command_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_pipetting_operations_machine_run_id_workflow_step_execution_id",
                table: "pipetting_operations",
                columns: new[] { "machine_run_id", "workflow_step_execution_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_resource_leases");

            migrationBuilder.DropTable(
                name: "needle_states");

            migrationBuilder.DropTable(
                name: "pipetting_operations");

            migrationBuilder.DropTable(
                name: "robot_arm_states");

            migrationBuilder.DropIndex(
                name: "IX_reagent_consumptions_device_command_execution_id_reagent_bottle_id",
                table: "reagent_consumptions");

            migrationBuilder.DropIndex(
                name: "IX_dispense_executions_device_command_execution_id_reagent_bottle_id",
                table: "dispense_executions");

            migrationBuilder.CreateIndex(
                name: "IX_dispense_executions_device_command_execution_id",
                table: "dispense_executions",
                column: "device_command_execution_id");
        }
    }
}
