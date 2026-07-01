using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DabLifecycleModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dab_batches_dab_mix_position_id",
                table: "dab_batches");

            migrationBuilder.AddColumn<string>(
                name: "active_dab_batch_id",
                table: "dab_mix_positions",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "dab_mix_positions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Available");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "dab_mix_positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "prepared_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "actual_prepared_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cleaning_confirmed_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cleaning_status",
                table: "dab_batches",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "dab_batches",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dab_a_ratio_parts",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "dab_a_reagent_bottle_id",
                table: "dab_batches",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dab_a_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "dab_b_ratio_parts",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "dab_b_reagent_bottle_id",
                table: "dab_batches",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dab_b_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "line_reserve_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 400);

            migrationBuilder.AddColumn<int>(
                name: "slide_count",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "total_required_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "used_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "volume_per_slide_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "water_ratio_parts",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 18);

            migrationBuilder.AddColumn<int>(
                name: "water_volume_ul",
                table: "dab_batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "workflow_step_execution_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36);

            migrationBuilder.AlterColumn<string>(
                name: "machine_run_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36);

            migrationBuilder.AddColumn<string>(
                name: "command_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "staining_task_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "machine_run_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36);

            migrationBuilder.AddColumn<string>(
                name: "command_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dab_batch_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reagent_bottle_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reservation_kind",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "MachineRun");

            migrationBuilder.AddColumn<string>(
                name: "source_role",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Reserved");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "reagent_reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dab_batch_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    dab_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    staining_task_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    required_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dab_batch_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_dab_batch_tasks_dab_batches_dab_batch_id",
                        column: x => x.dab_batch_id,
                        principalTable: "dab_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dab_batch_tasks_staining_tasks_staining_task_id",
                        column: x => x.staining_task_id,
                        principalTable: "staining_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                -- Existing DAB rows were created before the formal lifecycle model had source
                -- bottles, formula confirmation and cleaning semantics. Keep them readable, but
                -- fail closed by preventing automatic reuse under the new lifecycle rules.
                UPDATE dab_batches
                SET status = 'LegacyUnverified',
                    cleaning_status = 'NeedsManualResolution',
                    slide_count = 0,
                    volume_per_slide_ul = 0,
                    line_reserve_volume_ul = 0,
                    dab_a_ratio_parts = 0,
                    dab_b_ratio_parts = 0,
                    water_ratio_parts = 0,
                    total_required_volume_ul = 0,
                    actual_prepared_volume_ul = 0,
                    dab_a_volume_ul = 0,
                    dab_b_volume_ul = 0,
                    water_volume_ul = 0,
                    used_volume_ul = 0,
                    updated_at_utc = COALESCE(updated_at_utc, created_at_utc)
                WHERE dab_a_reagent_bottle_id IS NULL
                   OR dab_b_reagent_bottle_id IS NULL
                   OR actual_prepared_volume_ul = 0
                   OR total_required_volume_ul = 0;

                UPDATE reagent_reservations
                SET reservation_kind = 'MachineRun',
                    source_role = '',
                    status = 'Reserved'
                WHERE reservation_kind IS NULL OR reservation_kind = '';

                UPDATE dab_mix_positions
                SET status = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM dab_batches b
                            WHERE b.dab_mix_position_id = dab_mix_positions.id
                              AND b.status = 'LegacyUnverified')
                        THEN 'NeedsManualResolution'
                        WHEN EXISTS (
                            SELECT 1 FROM dab_batches b
                            WHERE b.dab_mix_position_id = dab_mix_positions.id
                              AND b.status IN ('Depleted', 'Expired', 'AwaitingCleaning'))
                        THEN 'AwaitingCleaning'
                        ELSE 'Occupied'
                    END,
                    active_dab_batch_id = (
                        SELECT b.id FROM dab_batches b
                        WHERE b.dab_mix_position_id = dab_mix_positions.id
                          AND b.status <> 'Cleaned'
                        ORDER BY b.created_at_utc DESC
                        LIMIT 1)
                WHERE EXISTS (
                    SELECT 1 FROM dab_batches b
                    WHERE b.dab_mix_position_id = dab_mix_positions.id
                      AND b.status <> 'Cleaned');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_dab_mix_positions_active_dab_batch_id",
                table: "dab_mix_positions",
                column: "active_dab_batch_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_created_by_user_id",
                table: "dab_batches",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_dab_a_reagent_bottle_id",
                table: "dab_batches",
                column: "dab_a_reagent_bottle_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_dab_b_reagent_bottle_id",
                table: "dab_batches",
                column: "dab_b_reagent_bottle_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_dab_mix_position_id",
                table: "dab_batches",
                column: "dab_mix_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_command_id",
                table: "dab_batch_usages",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_created_by_user_id",
                table: "dab_batch_usages",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_usages_staining_task_id",
                table: "dab_batch_usages",
                column: "staining_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_tasks_dab_batch_id_staining_task_id",
                table: "dab_batch_tasks",
                columns: new[] { "dab_batch_id", "staining_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dab_batch_tasks_staining_task_id",
                table: "dab_batch_tasks",
                column: "staining_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_reservations_command_id",
                table: "reagent_reservations",
                column: "command_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_reservations_created_by_user_id",
                table: "reagent_reservations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_reservations_dab_batch_id_source_role_status",
                table: "reagent_reservations",
                columns: new[] { "dab_batch_id", "source_role", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_reagent_reservations_reagent_bottle_id_status",
                table: "reagent_reservations",
                columns: new[] { "reagent_bottle_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_dab_batch_usages_staining_tasks_staining_task_id",
                table: "dab_batch_usages",
                column: "staining_task_id",
                principalTable: "staining_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_dab_batch_usages_users_created_by_user_id",
                table: "dab_batch_usages",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_dab_batches_reagent_bottles_dab_a_reagent_bottle_id",
                table: "dab_batches",
                column: "dab_a_reagent_bottle_id",
                principalTable: "reagent_bottles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_dab_batches_reagent_bottles_dab_b_reagent_bottle_id",
                table: "dab_batches",
                column: "dab_b_reagent_bottle_id",
                principalTable: "reagent_bottles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_dab_batches_users_created_by_user_id",
                table: "dab_batches",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_reagent_reservations_dab_batches_dab_batch_id",
                table: "reagent_reservations",
                column: "dab_batch_id",
                principalTable: "dab_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_reagent_reservations_reagent_bottles_reagent_bottle_id",
                table: "reagent_reservations",
                column: "reagent_bottle_id",
                principalTable: "reagent_bottles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reagent_reservations_users_created_by_user_id",
                table: "reagent_reservations",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_dab_batch_usages_staining_tasks_staining_task_id",
                table: "dab_batch_usages");

            migrationBuilder.DropForeignKey(
                name: "FK_dab_batch_usages_users_created_by_user_id",
                table: "dab_batch_usages");

            migrationBuilder.DropForeignKey(
                name: "FK_dab_batches_reagent_bottles_dab_a_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_dab_batches_reagent_bottles_dab_b_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_dab_batches_users_created_by_user_id",
                table: "dab_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_reagent_reservations_dab_batches_dab_batch_id",
                table: "reagent_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_reagent_reservations_reagent_bottles_reagent_bottle_id",
                table: "reagent_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_reagent_reservations_users_created_by_user_id",
                table: "reagent_reservations");

            migrationBuilder.DropTable(
                name: "dab_batch_tasks");

            migrationBuilder.DropIndex(
                name: "IX_dab_mix_positions_active_dab_batch_id",
                table: "dab_mix_positions");

            migrationBuilder.DropIndex(
                name: "IX_dab_batches_created_by_user_id",
                table: "dab_batches");

            migrationBuilder.DropIndex(
                name: "IX_dab_batches_dab_a_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropIndex(
                name: "IX_dab_batches_dab_b_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropIndex(
                name: "IX_dab_batches_dab_mix_position_id",
                table: "dab_batches");

            migrationBuilder.DropIndex(
                name: "IX_dab_batch_usages_command_id",
                table: "dab_batch_usages");

            migrationBuilder.DropIndex(
                name: "IX_dab_batch_usages_created_by_user_id",
                table: "dab_batch_usages");

            migrationBuilder.DropIndex(
                name: "IX_dab_batch_usages_staining_task_id",
                table: "dab_batch_usages");

            migrationBuilder.DropIndex(
                name: "IX_reagent_reservations_command_id",
                table: "reagent_reservations");

            migrationBuilder.DropIndex(
                name: "IX_reagent_reservations_created_by_user_id",
                table: "reagent_reservations");

            migrationBuilder.DropIndex(
                name: "IX_reagent_reservations_dab_batch_id_source_role_status",
                table: "reagent_reservations");

            migrationBuilder.DropIndex(
                name: "IX_reagent_reservations_reagent_bottle_id_status",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "active_dab_batch_id",
                table: "dab_mix_positions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "dab_mix_positions");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "dab_mix_positions");

            migrationBuilder.DropColumn(
                name: "actual_prepared_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "cleaning_confirmed_at_utc",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "cleaning_status",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_a_ratio_parts",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_a_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_a_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_b_ratio_parts",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_b_reagent_bottle_id",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "dab_b_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "line_reserve_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "slide_count",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "total_required_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "used_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "volume_per_slide_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "water_ratio_parts",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "water_volume_ul",
                table: "dab_batches");

            migrationBuilder.DropColumn(
                name: "command_id",
                table: "dab_batch_usages");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "dab_batch_usages");

            migrationBuilder.DropColumn(
                name: "staining_task_id",
                table: "dab_batch_usages");

            migrationBuilder.DropColumn(
                name: "command_id",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "dab_batch_id",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "reagent_bottle_id",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "reservation_kind",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "source_role",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "status",
                table: "reagent_reservations");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "reagent_reservations");

            migrationBuilder.AlterColumn<string>(
                name: "machine_run_id",
                table: "reagent_reservations",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "prepared_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at_utc",
                table: "dab_batches",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "workflow_step_execution_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "machine_run_id",
                table: "dab_batch_usages",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_dab_batches_dab_mix_position_id",
                table: "dab_batches",
                column: "dab_mix_position_id");
        }
    }
}
