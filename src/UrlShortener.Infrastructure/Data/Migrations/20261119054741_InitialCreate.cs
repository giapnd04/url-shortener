using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_clients",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    api_key_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "short_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    target_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    client_id = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    disabled_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_short_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_short_links_api_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "api_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_api_clients_api_key_hash",
                table: "api_clients",
                column: "api_key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_short_links_client_id_created_at",
                table: "short_links",
                columns: new[] { "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "UX_short_links_code",
                table: "short_links",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "short_links");

            migrationBuilder.DropTable(
                name: "api_clients");
        }
    }
}
