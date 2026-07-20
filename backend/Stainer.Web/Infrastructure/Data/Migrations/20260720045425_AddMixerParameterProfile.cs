using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMixerParameterProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mixer_parameter_profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    drawer_code = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    origin = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    start_stroke = table.Column<int>(type: "INTEGER", nullable: true),
                    total_stroke = table.Column<int>(type: "INTEGER", nullable: true),
                    top_dwell_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    bottom_dwell_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    forward_speed = table.Column<int>(type: "INTEGER", nullable: true),
                    reverse_speed = table.Column<int>(type: "INTEGER", nullable: true),
                    target_cycles = table.Column<int>(type: "INTEGER", nullable: true),
                    remaining_cycles = table.Column<int>(type: "INTEGER", nullable: true),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mixer_parameter_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mixer_parameter_profiles_drawer_code",
                table: "mixer_parameter_profiles",
                column: "drawer_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mixer_parameter_profiles");
        }
    }
}
