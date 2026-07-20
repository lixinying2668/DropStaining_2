using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWashValveConfigProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wash_valve_config_profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    scope_key = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    wash_temp_c = table.Column<decimal>(type: "TEXT", nullable: true),
                    solenoid_open = table.Column<bool>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wash_valve_config_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wash_valve_config_profiles_scope_key",
                table: "wash_valve_config_profiles",
                column: "scope_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wash_valve_config_profiles");
        }
    }
}
