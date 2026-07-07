using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessMvp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WhiteSlotToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlackSlotToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentFen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Turn = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: true),
                    ResultReason = table.Column<int>(type: "int", nullable: true),
                    HalfmoveClock = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Moves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoveNumber = table.Column<int>(type: "int", nullable: false),
                    PlyColor = table.Column<int>(type: "int", nullable: false),
                    San = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FromSquare = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ToSquare = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    PromotionPiece = table.Column<int>(type: "int", nullable: true),
                    ResultingFen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsCheck = table.Column<bool>(type: "bit", nullable: false),
                    IsCheckmate = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Moves_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Moves_GameId_MoveNumber",
                table: "Moves",
                columns: new[] { "GameId", "MoveNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Moves");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
