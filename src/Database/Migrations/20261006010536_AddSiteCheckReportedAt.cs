using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteChecker.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteCheckReportedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedAt",
                table: "SiteChecks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportedAt",
                table: "SiteChecks");
        }
    }
}
