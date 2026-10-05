using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteChecker.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteCheckQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SiteChecks_Status_StartDate",
                table: "SiteChecks",
                columns: new[] { "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SiteChecks_Status_StartDate",
                table: "SiteChecks");
        }
    }
}
