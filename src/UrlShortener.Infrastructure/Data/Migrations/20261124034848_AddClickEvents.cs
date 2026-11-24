using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClickEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "click_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    link_id = table.Column<long>(type: "bigint", nullable: false),
                    clicked_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    referrer_host = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    ua_family = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_click_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_click_events_short_links_link_id",
                        column: x => x.link_id,
                        principalTable: "short_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_click_events_link_id_clicked_at",
                table: "click_events",
                columns: new[] { "link_id", "clicked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "click_events");
        }
    }
}
