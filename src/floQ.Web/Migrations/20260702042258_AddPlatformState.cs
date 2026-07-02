using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace floQ.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ShutoffActive = table.Column<bool>(type: "boolean", nullable: false),
                    ShutoffReason = table.Column<string>(type: "text", nullable: false),
                    ShutoffAt = table.Column<string>(type: "text", nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformStates");
        }
    }
}
