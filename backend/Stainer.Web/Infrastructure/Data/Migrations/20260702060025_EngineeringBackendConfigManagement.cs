using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringBackendConfigManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_communication_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    device_mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    adapter_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    module_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    actor = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ok = table.Column<bool>(type: "INTEGER", nullable: false),
                    acknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    request_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    response_json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_communication_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "engineering_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    target = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    dangerous_operation_confirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    authenticated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_sessions", x => x.id);
                    table.CheckConstraint("ck_engineering_sessions_status", "status in ('Active', 'Expired', 'Revoked')");
                    table.ForeignKey(
                        name: "FK_engineering_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_communication_records_command_id",
                table: "device_communication_records",
                column: "command_id");

            migrationBuilder.CreateIndex(
                name: "IX_device_communication_records_created_at_utc",
                table: "device_communication_records",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_device_communication_records_module_code_status_created_at_utc",
                table: "device_communication_records",
                columns: new[] { "module_code", "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_engineering_sessions_command_id",
                table: "engineering_sessions",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_sessions_user_id_status_expires_at_utc",
                table: "engineering_sessions",
                columns: new[] { "user_id", "status", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_communication_records");

            migrationBuilder.DropTable(
                name: "engineering_sessions");
        }
    }
}
