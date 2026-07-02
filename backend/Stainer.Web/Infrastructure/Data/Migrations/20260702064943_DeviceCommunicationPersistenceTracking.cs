using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceCommunicationPersistenceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "persistence_attempt_count",
                table: "device_communication_records",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "persistence_completed_at_utc",
                table: "device_communication_records",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "persistence_failure_reason",
                table: "device_communication_records",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "persistence_last_attempt_at_utc",
                table: "device_communication_records",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "persistence_status",
                table: "device_communication_records",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Complete");

            migrationBuilder.Sql(
                """
                UPDATE device_communication_records
                SET persistence_status = 'Complete',
                    persistence_attempt_count = 1,
                    persistence_last_attempt_at_utc = completed_at_utc,
                    persistence_completed_at_utc = completed_at_utc;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_device_communication_records_persistence_status_created_at_utc",
                table: "device_communication_records",
                columns: new[] { "persistence_status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_device_communication_records_persistence_status_created_at_utc",
                table: "device_communication_records");

            migrationBuilder.DropColumn(
                name: "persistence_attempt_count",
                table: "device_communication_records");

            migrationBuilder.DropColumn(
                name: "persistence_completed_at_utc",
                table: "device_communication_records");

            migrationBuilder.DropColumn(
                name: "persistence_failure_reason",
                table: "device_communication_records");

            migrationBuilder.DropColumn(
                name: "persistence_last_attempt_at_utc",
                table: "device_communication_records");

            migrationBuilder.DropColumn(
                name: "persistence_status",
                table: "device_communication_records");
        }
    }
}
