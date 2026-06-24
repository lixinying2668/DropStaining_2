using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeLedgerAndMockExecutor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dab_batches",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    dab_mix_position_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    position_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    remaining_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    prepared_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dab_batches", x => x.id);
                    table.ForeignKey(
                        name: "FK_dab_batches_dab_mix_positions_dab_mix_position_id",
                        column: x => x.dab_mix_position_id,
                        principalTable: "dab_mix_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "machine_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    run_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    requested_by_user_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    pause_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    stop_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    fault_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    current_major_step_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_machine_runs_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "alarms",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    cleared_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarms", x => x.id);
                    table.ForeignKey(
                        name: "FK_alarms_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_batches",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    drawer_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    drawer_code = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_batches", x => x.id);
                    table.ForeignKey(
                        name: "FK_channel_batches_drawers_drawer_id",
                        column: x => x.drawer_id,
                        principalTable: "drawers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_channel_batches_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reagent_reservations",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    required_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    reserved_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_reservations_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alarm_actions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    alarm_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    actor_user_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarm_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_alarm_actions_alarms_alarm_id",
                        column: x => x.alarm_id,
                        principalTable: "alarms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alarm_actions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "slide_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    channel_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    staining_task_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    physical_slot_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    slot_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    task_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slide_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_slide_tasks_channel_batches_channel_batch_id",
                        column: x => x.channel_batch_id,
                        principalTable: "channel_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_slide_tasks_physical_slots_physical_slot_id",
                        column: x => x.physical_slot_id,
                        principalTable: "physical_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_slide_tasks_staining_tasks_staining_task_id",
                        column: x => x.staining_task_id,
                        principalTable: "staining_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    slide_task_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_executions_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_executions_slide_tasks_slide_task_id",
                        column: x => x.slide_task_id,
                        principalTable: "slide_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_executions_workflow_versions_workflow_version_id",
                        column: x => x.workflow_version_id,
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    step_no = table.Column<int>(type: "INTEGER", nullable: false),
                    major_step_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    step_name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    action_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    redo_count = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_step_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_step_executions_workflow_executions_workflow_execution_id",
                        column: x => x.workflow_execution_id,
                        principalTable: "workflow_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dab_batch_usages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    dab_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dab_batch_usages", x => x.id);
                    table.ForeignKey(
                        name: "FK_dab_batch_usages_dab_batches_dab_batch_id",
                        column: x => x.dab_batch_id,
                        principalTable: "dab_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dab_batch_usages_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dab_batch_usages_workflow_step_executions_workflow_step_execution_id",
                        column: x => x.workflow_step_execution_id,
                        principalTable: "workflow_step_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_command_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    command_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    result_json = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    command_sent_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    acknowledged_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_command_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_command_executions_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_command_executions_workflow_step_executions_workflow_step_execution_id",
                        column: x => x.workflow_step_execution_id,
                        principalTable: "workflow_step_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reagent_consumptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    machine_run_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_step_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_bottle_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_consumptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_consumptions_machine_runs_machine_run_id",
                        column: x => x.machine_run_id,
                        principalTable: "machine_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reagent_consumptions_reagent_bottles_reagent_bottle_id",
                        column: x => x.reagent_bottle_id,
                        principalTable: "reagent_bottles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reagent_consumptions_workflow_step_executions_workflow_step_execution_id",
                        column: x => x.workflow_step_execution_id,
                        principalTable: "workflow_step_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dispense_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    device_command_execution_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_bottle_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    source_position_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    target_slot_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispense_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_dispense_executions_device_command_executions_device_command_execution_id",
                        column: x => x.device_command_execution_id,
                        principalTable: "device_command_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dispense_executions_reagent_bottles_reagent_bottle_id",
                        column: x => x.reagent_bottle_id,
                        principalTable: "reagent_bottles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alarm_actions_actor_user_id",
                table: "alarm_actions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_actions_alarm_id",
                table: "alarm_actions",
                column: "alarm_id");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_machine_run_id_code_status",
                table: "alarms",
                columns: new[] { "machine_run_id", "code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_channel_batches_drawer_id",
                table: "channel_batches",
                column: "drawer_id");

            migrationBuilder.CreateIndex(
                name: "IX_channel_batches_machine_run_id_drawer_code",
                table: "channel_batches",
                columns: new[] { "machine_run_id", "drawer_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_dab_batch_id",
                table: "dab_batch_usages",
                column: "dab_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_machine_run_id",
                table: "dab_batch_usages",
                column: "machine_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_workflow_step_execution_id",
                table: "dab_batch_usages",
                column: "workflow_step_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_dab_mix_position_id",
                table: "dab_batches",
                column: "dab_mix_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_position_code_status",
                table: "dab_batches",
                columns: new[] { "position_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_device_command_executions_machine_run_id",
                table: "device_command_executions",
                column: "machine_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_command_executions_workflow_step_execution_id",
                table: "device_command_executions",
                column: "workflow_step_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_dispense_executions_device_command_execution_id",
                table: "dispense_executions",
                column: "device_command_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_dispense_executions_reagent_bottle_id",
                table: "dispense_executions",
                column: "reagent_bottle_id");

            migrationBuilder.CreateIndex(
                name: "IX_machine_runs_requested_by_user_id",
                table: "machine_runs",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_machine_runs_run_code",
                table: "machine_runs",
                column: "run_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_runs_status",
                table: "machine_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_consumptions_machine_run_id",
                table: "reagent_consumptions",
                column: "machine_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_consumptions_reagent_bottle_id",
                table: "reagent_consumptions",
                column: "reagent_bottle_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_consumptions_workflow_step_execution_id",
                table: "reagent_consumptions",
                column: "workflow_step_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_reservations_machine_run_id_reagent_code",
                table: "reagent_reservations",
                columns: new[] { "machine_run_id", "reagent_code" });

            migrationBuilder.CreateIndex(
                name: "IX_slide_tasks_channel_batch_id",
                table: "slide_tasks",
                column: "channel_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_slide_tasks_physical_slot_id",
                table: "slide_tasks",
                column: "physical_slot_id");

            migrationBuilder.CreateIndex(
                name: "IX_slide_tasks_staining_task_id",
                table: "slide_tasks",
                column: "staining_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_machine_run_id",
                table: "workflow_executions",
                column: "machine_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_slide_task_id",
                table: "workflow_executions",
                column: "slide_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workflow_version_id",
                table: "workflow_executions",
                column: "workflow_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_step_executions_workflow_execution_id_step_no",
                table: "workflow_step_executions",
                columns: new[] { "workflow_execution_id", "step_no" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_actions");

            migrationBuilder.DropTable(
                name: "dab_batch_usages");

            migrationBuilder.DropTable(
                name: "dispense_executions");

            migrationBuilder.DropTable(
                name: "reagent_consumptions");

            migrationBuilder.DropTable(
                name: "reagent_reservations");

            migrationBuilder.DropTable(
                name: "alarms");

            migrationBuilder.DropTable(
                name: "dab_batches");

            migrationBuilder.DropTable(
                name: "device_command_executions");

            migrationBuilder.DropTable(
                name: "workflow_step_executions");

            migrationBuilder.DropTable(
                name: "workflow_executions");

            migrationBuilder.DropTable(
                name: "slide_tasks");

            migrationBuilder.DropTable(
                name: "channel_batches");

            migrationBuilder.DropTable(
                name: "machine_runs");
        }
    }
}
