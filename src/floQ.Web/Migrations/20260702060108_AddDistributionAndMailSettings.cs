using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace floQ.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributionAndMailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttachPdf = table.Column<bool>(type: "boolean", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstOpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenCount = table.Column<int>(type: "integer", nullable: false),
                    FirstDownloadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false),
                    PdfFilePath = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentDistributions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantMailSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Sender = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SenderDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMailSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSecrets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedValue = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSecrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributions_DocumentId",
                table: "DocumentDistributions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributions_TenantId",
                table: "DocumentDistributions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributions_Token",
                table: "DocumentDistributions",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMailSettings_TenantId",
                table: "TenantMailSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSecrets_TenantId",
                table: "TenantSecrets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSecrets_TenantId_Provider_Key",
                table: "TenantSecrets",
                columns: new[] { "TenantId", "Provider", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentDistributions");

            migrationBuilder.DropTable(
                name: "TenantMailSettings");

            migrationBuilder.DropTable(
                name: "TenantSecrets");
        }
    }
}
