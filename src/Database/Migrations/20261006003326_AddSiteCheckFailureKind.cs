using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteChecker.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteCheckFailureKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureKind",
                table: "SiteChecks",
                type: "INTEGER",
                nullable: true);

            // Backfill from the exception type name the old code recorded in Metadata, using the
            // same suffix matching it used to detect Known Failures. Failed (4) checks without a
            // recorded type were unexpected errors. Known = 2, Unexpected = 1.
            migrationBuilder.Sql("""
                UPDATE SiteChecks
                SET FailureKind = CASE
                    WHEN json_extract(Metadata, '$.EXCEPTION_TYPE') LIKE '%KnownScraperException'
                      OR json_extract(Metadata, '$.EXCEPTION_TYPE') LIKE '%AccessDeniedScraperException'
                      OR json_extract(Metadata, '$.EXCEPTION_TYPE') LIKE '%BlankPageScraperException'
                    THEN 2
                    ELSE 1
                END
                WHERE Status = 4;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "SiteChecks");
        }
    }
}
