using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace uSLearn.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "ProcessedIntegrationEvents",
                schema: "identity",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandlerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedIntegrationEvents", x => new { x.EventId, x.HandlerName });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedIntegrationEvents_EventId",
                schema: "identity",
                table: "ProcessedIntegrationEvents",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedIntegrationEvents_ProcessedAt",
                schema: "identity",
                table: "ProcessedIntegrationEvents",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedIntegrationEvents",
                schema: "identity");
        }
    }
}
