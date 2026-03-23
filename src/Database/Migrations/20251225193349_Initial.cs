using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteChecker.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScraperId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    UseVpn = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlwaysTakeScreenshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    KnownFailuresThreshold = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    DiscordConfig = table.Column<string>(type: "TEXT", nullable: false),
                    PushoverConfig = table.Column<string>(type: "TEXT", nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    VpnLocationId = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DoneDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteChecks_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteCheckScreenshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SiteCheckId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteCheckScreenshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteCheckScreenshots_SiteChecks_SiteCheckId",
                        column: x => x.SiteCheckId,
                        principalTable: "SiteChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteChecks_SiteId",
                table: "SiteChecks",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteCheckScreenshots_SiteCheckId",
                table: "SiteCheckScreenshots",
                column: "SiteCheckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_ScraperId",
                table: "Sites",
                column: "ScraperId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteCheckScreenshots");

            migrationBuilder.DropTable(
                name: "SiteChecks");

            migrationBuilder.DropTable(
                name: "Sites");
        }
    }
}
