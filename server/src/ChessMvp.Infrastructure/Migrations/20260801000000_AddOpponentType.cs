using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessMvp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpponentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The opposing seat is either a waiting human (the share-link flow) or the built-in
            // AI (single-user play). Existing rows predate the column, so default them to Human
            // (enum value 0) to preserve the original two-player behaviour.
            migrationBuilder.AddColumn<int>(
                name: "OpponentType",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpponentType",
                table: "Games");
        }
    }
}
