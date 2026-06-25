using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowAssignmentHistoryOperatorCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_assignment_history_channel_batches_channel_batch_id",
                table: "workflow_assignment_history");

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "workflow_assignment_history",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_user_id",
                table: "workflow_assignment_history",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE workflow_assignment_history
                SET operator_user_id = actor_user_id
                WHERE operator_user_id IS NULL AND actor_user_id IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE workflow_assignment_history
                SET action_type = 'Locked'
                WHERE action_type = 'Lock';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_history_action_type",
                table: "workflow_assignment_history",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_history_channel_batch_id_action_type_created_at_utc",
                table: "workflow_assignment_history",
                columns: new[] { "channel_batch_id", "action_type", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_history_correlation_id",
                table: "workflow_assignment_history",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_history_created_at_utc",
                table: "workflow_assignment_history",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_assignment_history_operator_user_id",
                table: "workflow_assignment_history",
                column: "operator_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_assignment_history_channel_batches_channel_batch_id",
                table: "workflow_assignment_history",
                column: "channel_batch_id",
                principalTable: "channel_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_assignment_history_users_operator_user_id",
                table: "workflow_assignment_history",
                column: "operator_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_assignment_history_channel_batches_channel_batch_id",
                table: "workflow_assignment_history");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_assignment_history_users_operator_user_id",
                table: "workflow_assignment_history");

            migrationBuilder.DropIndex(
                name: "IX_workflow_assignment_history_action_type",
                table: "workflow_assignment_history");

            migrationBuilder.DropIndex(
                name: "IX_workflow_assignment_history_channel_batch_id_action_type_created_at_utc",
                table: "workflow_assignment_history");

            migrationBuilder.DropIndex(
                name: "IX_workflow_assignment_history_correlation_id",
                table: "workflow_assignment_history");

            migrationBuilder.DropIndex(
                name: "IX_workflow_assignment_history_created_at_utc",
                table: "workflow_assignment_history");

            migrationBuilder.DropIndex(
                name: "IX_workflow_assignment_history_operator_user_id",
                table: "workflow_assignment_history");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "workflow_assignment_history");

            migrationBuilder.DropColumn(
                name: "operator_user_id",
                table: "workflow_assignment_history");

            migrationBuilder.Sql(
                """
                UPDATE workflow_assignment_history
                SET action_type = 'Lock'
                WHERE action_type = 'Locked';
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_assignment_history_channel_batches_channel_batch_id",
                table: "workflow_assignment_history",
                column: "channel_batch_id",
                principalTable: "channel_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
