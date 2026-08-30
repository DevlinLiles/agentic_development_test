using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessMvp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiOpponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpponentType",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiColor",
                table: "Games",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiColor",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "OpponentType",
                table: "Games");
        }
    }
}
