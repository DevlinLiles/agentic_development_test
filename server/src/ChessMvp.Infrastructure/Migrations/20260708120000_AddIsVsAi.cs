using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessMvp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsVsAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVsAi",
                table: "Games",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVsAi",
                table: "Games");
        }
    }
}
