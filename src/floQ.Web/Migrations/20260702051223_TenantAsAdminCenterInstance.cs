using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace floQ.Web.Migrations
{
    /// <inheritdoc />
    public partial class TenantAsAdminCenterInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformStates");

            // Reiner AC-Cache ohne Tenant-Zuordnung — leeren statt migrieren,
            // der nächste Sync füllt ihn tenant-scoped neu.
            migrationBuilder.Sql("""DELETE FROM "EnabledModules";""");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EnabledModules",
                table: "EnabledModules");

            migrationBuilder.AddColumn<bool>(
                name: "ShutoffActive",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShutoffAt",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShutoffReason",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EnabledModules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_EnabledModules",
                table: "EnabledModules",
                columns: new[] { "TenantId", "Key" });

            // Backfill für Bestands-Tenants (vor dem Unique-Index): deterministischer
            // Slug aus der Id (12 Hex-Zeichen, lowercase) — stabil und kollisionsfrei.
            migrationBuilder.Sql("""UPDATE "Tenants" SET "Slug" = left(replace("Id"::text, '-', ''), 12) WHERE "Slug" = '';""");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EnabledModules",
                table: "EnabledModules");

            migrationBuilder.DropColumn(
                name: "ShutoffActive",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ShutoffAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ShutoffReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EnabledModules");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EnabledModules",
                table: "EnabledModules",
                column: "Key");

            migrationBuilder.CreateTable(
                name: "PlatformStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShutoffActive = table.Column<bool>(type: "boolean", nullable: false),
                    ShutoffAt = table.Column<string>(type: "text", nullable: false),
                    ShutoffReason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformStates", x => x.Id);
                });
        }
    }
}
