using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onlyspans.Worker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deployment_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deployment_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    log_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deployment_result",
                columns: table => new
                {
                    deployment_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_result", x => x.deployment_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deployment_log_deployment_id",
                table: "deployment_log",
                column: "deployment_id");

            migrationBuilder.CreateIndex(
                name: "IX_deployment_log_deployment_id_timestamp",
                table: "deployment_log",
                columns: new[] { "deployment_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_deployment_log_timestamp",
                table: "deployment_log",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_deployment_result_started_at",
                table: "deployment_result",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_deployment_result_status",
                table: "deployment_result",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deployment_log");

            migrationBuilder.DropTable(
                name: "deployment_result");
        }
    }
}
