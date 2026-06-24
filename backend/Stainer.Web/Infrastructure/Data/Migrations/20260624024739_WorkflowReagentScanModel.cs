using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stainer.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowReagentScanModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "liquid_class_profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    aspirate_speed_ul_per_second = table.Column<int>(type: "INTEGER", nullable: true),
                    dispense_speed_ul_per_second = table.Column<int>(type: "INTEGER", nullable: true),
                    pre_wet_cycles = table.Column<int>(type: "INTEGER", nullable: true),
                    mix_cycles = table.Column<int>(type: "INTEGER", nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liquid_class_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reagent_scan_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    session_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_by_user_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_scan_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_scan_sessions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    workflow_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reagent_definitions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    liquid_class_profile_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_definitions_liquid_class_profiles_liquid_class_profile_id",
                        column: x => x.liquid_class_profile_id,
                        principalTable: "liquid_class_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reagent_scan_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_scan_session_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_rack_position_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    scanner_channel_no = table.Column<int>(type: "INTEGER", nullable: false),
                    scanner_channel_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    locator_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    scan_result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    raw_barcode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    parsed_reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    parsed_quantity_ul = table.Column<int>(type: "INTEGER", nullable: true),
                    parsed_batch_no = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    parsed_serial_no = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    is_validation_passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    validation_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_scan_items", x => x.id);
                    table.CheckConstraint("ck_reagent_scan_items_scan_result", "scan_result in ('EMPTY', 'VALID', 'INVALID')");
                    table.ForeignKey(
                        name: "FK_reagent_scan_items_reagent_rack_positions_reagent_rack_position_id",
                        column: x => x.reagent_rack_position_id,
                        principalTable: "reagent_rack_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reagent_scan_items_reagent_scan_sessions_reagent_scan_session_id",
                        column: x => x.reagent_scan_session_id,
                        principalTable: "reagent_scan_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_definition_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    version_no = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    change_note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.id);
                    table.CheckConstraint("ck_workflow_versions_status", "status in ('Draft', 'Published', 'Retired')");
                    table.ForeignKey(
                        name: "FK_workflow_versions_workflow_definitions_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reagent_bottles",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_definition_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    full_barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    production_batch_no = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    serial_no = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    initial_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    remaining_volume_ul = table.Column<int>(type: "INTEGER", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    first_scanned_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    last_scanned_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_bottles", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_bottles_reagent_definitions_reagent_definition_id",
                        column: x => x.reagent_definition_id,
                        principalTable: "reagent_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "primary_antibody_workflow_mappings",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    primary_antibody_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    workflow_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_primary_antibody_workflow_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_primary_antibody_workflow_mappings_workflow_versions_workflow_version_id",
                        column: x => x.workflow_version_id,
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_reagent_requirements",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    required_volume_ul = table.Column<int>(type: "INTEGER", nullable: true),
                    is_required = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_reagent_requirements", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_reagent_requirements_workflow_versions_workflow_version_id",
                        column: x => x.workflow_version_id,
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_version_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    step_no = table.Column<int>(type: "INTEGER", nullable: false),
                    major_step_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    action_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    reagent_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    volume_ul = table.Column<int>(type: "INTEGER", nullable: true),
                    duration_seconds = table.Column<int>(type: "INTEGER", nullable: true),
                    target_temperature_deci_c = table.Column<int>(type: "INTEGER", nullable: true),
                    mix_parameters_json = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    wash_parameters_json = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    failure_strategy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_steps_workflow_versions_workflow_version_id",
                        column: x => x.workflow_version_id,
                        principalTable: "workflow_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reagent_rack_placements",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_bottle_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_rack_position_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    reagent_scan_session_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    placed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    removed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reagent_rack_placements", x => x.id);
                    table.ForeignKey(
                        name: "FK_reagent_rack_placements_reagent_bottles_reagent_bottle_id",
                        column: x => x.reagent_bottle_id,
                        principalTable: "reagent_bottles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reagent_rack_placements_reagent_rack_positions_reagent_rack_position_id",
                        column: x => x.reagent_rack_position_id,
                        principalTable: "reagent_rack_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reagent_rack_placements_reagent_scan_sessions_reagent_scan_session_id",
                        column: x => x.reagent_scan_session_id,
                        principalTable: "reagent_scan_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_liquid_class_profiles_code",
                table: "liquid_class_profiles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_primary_antibody_workflow_mappings_primary_antibody_code",
                table: "primary_antibody_workflow_mappings",
                column: "primary_antibody_code");

            migrationBuilder.CreateIndex(
                name: "IX_primary_antibody_workflow_mappings_primary_antibody_code_workflow_version_id",
                table: "primary_antibody_workflow_mappings",
                columns: new[] { "primary_antibody_code", "workflow_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_primary_antibody_workflow_mappings_workflow_version_id",
                table: "primary_antibody_workflow_mappings",
                column: "workflow_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_bottles_full_barcode",
                table: "reagent_bottles",
                column: "full_barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reagent_bottles_reagent_code_production_batch_no_serial_no",
                table: "reagent_bottles",
                columns: new[] { "reagent_code", "production_batch_no", "serial_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reagent_bottles_reagent_definition_id",
                table: "reagent_bottles",
                column: "reagent_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_definitions_liquid_class_profile_id",
                table: "reagent_definitions",
                column: "liquid_class_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_definitions_reagent_code",
                table: "reagent_definitions",
                column: "reagent_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reagent_rack_placements_reagent_bottle_id",
                table: "reagent_rack_placements",
                column: "reagent_bottle_id",
                unique: true,
                filter: "removed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_rack_placements_reagent_bottle_id_placed_at_utc",
                table: "reagent_rack_placements",
                columns: new[] { "reagent_bottle_id", "placed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_reagent_rack_placements_reagent_rack_position_id",
                table: "reagent_rack_placements",
                column: "reagent_rack_position_id",
                unique: true,
                filter: "removed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_rack_placements_reagent_scan_session_id",
                table: "reagent_rack_placements",
                column: "reagent_scan_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_items_parsed_reagent_code",
                table: "reagent_scan_items",
                column: "parsed_reagent_code");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_items_raw_barcode",
                table: "reagent_scan_items",
                column: "raw_barcode");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_items_reagent_rack_position_id",
                table: "reagent_scan_items",
                column: "reagent_rack_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_items_reagent_scan_session_id_reagent_rack_position_id",
                table: "reagent_scan_items",
                columns: new[] { "reagent_scan_session_id", "reagent_rack_position_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_items_scan_result",
                table: "reagent_scan_items",
                column: "scan_result");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_sessions_created_by_user_id",
                table: "reagent_scan_sessions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_sessions_session_code",
                table: "reagent_scan_sessions",
                column: "session_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reagent_scan_sessions_started_at_utc",
                table: "reagent_scan_sessions",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_code",
                table: "workflow_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_workflow_type",
                table: "workflow_definitions",
                column: "workflow_type");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reagent_requirements_reagent_code",
                table: "workflow_reagent_requirements",
                column: "reagent_code");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reagent_requirements_workflow_version_id_reagent_code",
                table: "workflow_reagent_requirements",
                columns: new[] { "workflow_version_id", "reagent_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_steps_reagent_code",
                table: "workflow_steps",
                column: "reagent_code");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_steps_workflow_version_id_step_no",
                table: "workflow_steps",
                columns: new[] { "workflow_version_id", "step_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_status",
                table: "workflow_versions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_workflow_definition_id_version_no",
                table: "workflow_versions",
                columns: new[] { "workflow_definition_id", "version_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "primary_antibody_workflow_mappings");

            migrationBuilder.DropTable(
                name: "reagent_rack_placements");

            migrationBuilder.DropTable(
                name: "reagent_scan_items");

            migrationBuilder.DropTable(
                name: "workflow_reagent_requirements");

            migrationBuilder.DropTable(
                name: "workflow_steps");

            migrationBuilder.DropTable(
                name: "reagent_bottles");

            migrationBuilder.DropTable(
                name: "reagent_scan_sessions");

            migrationBuilder.DropTable(
                name: "workflow_versions");

            migrationBuilder.DropTable(
                name: "reagent_definitions");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "liquid_class_profiles");
        }
    }
}
