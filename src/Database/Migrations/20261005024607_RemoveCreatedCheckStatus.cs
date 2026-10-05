using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteChecker.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCreatedCheckStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CheckStatus.Created (0) was retired; every Site Check now starts as Queued (1).
            migrationBuilder.Sql("UPDATE SiteChecks SET Status = 1 WHERE Status = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: Queued (1) is also a valid status in the previous schema.
        }
    }
}
