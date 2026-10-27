using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessMvp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpponentModeToGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Persists the per-game opponent-mode discriminator (0 = Human, 1 = Computer).
            //
            // Deliberately additive and non-destructive: a new NULLABLE column with a column-level
            // default, so existing rows are never rewritten — SQL Server leaves them NULL and the
            // DEFAULT only applies to future inserts that omit the column. Rows written before the
            // discriminator existed therefore keep NULL, which every read path normalizes to the
            // pre-existing two-player behavior (OpponentMode.Human) instead of failing or mutating
            // stored data.
            //
            // The column-level default is intentionally NOT mirrored as a model-level
            // HasDefaultValue: the model snapshot must stay exactly what EF would generate from
            // the current model, so no future migration emits a spurious AlterColumn for it, and
            // EF keeps treating the property as an ordinary (always-explicitly-written) column
            // rather than a store-generated one.
            migrationBuilder.AddColumn<int>(
                name: "OpponentMode",
                table: "Games",
                type: "int",
                nullable: true,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpponentMode",
                table: "Games");
        }
    }
}
