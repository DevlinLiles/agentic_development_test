using ChessMvp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChessMvp.Infrastructure.Persistence;

public class ChessDbContext : DbContext
{
    public ChessDbContext(DbContextOptions<ChessDbContext> options)
        : base(options)
    {
    }

    public DbSet<Game> Games => Set<Game>();

    public DbSet<Move> Moves => Set<Move>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(builder =>
        {
            builder.HasKey(g => g.Id);

            // Persists the per-game opponent-mode discriminator as a plain nullable int column,
            // mapped exactly like every other enum on this entity (Turn, Status, Result,
            // ResultReason, ...) - no value converter, no model-level default.
            //
            // Nullable so the column could be introduced additively (see AddOpponentModeToGame):
            // rows written before the discriminator existed keep NULL instead of being rewritten,
            // and readers normalize NULL to OpponentMode.Human - the pre-existing two-player
            // behavior - rather than surfacing a third "unknown" mode.
            //
            // The DEFAULT 0 lives in the migration SQL only (a database-level safety net for
            // inserts made outside EF); it is deliberately not declared here, so the model
            // snapshot stays exactly in sync with the model and no future migration emits a
            // spurious AlterColumn for it. EF always writes an explicit value anyway, because
            // the Game entity seeds Human on construction.
            builder.Property(g => g.OpponentMode)
                .IsRequired(false)
                .HasColumnType("int");

            builder.Property(g => g.CurrentFen)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(g => g.Version)
                .IsRowVersion();

            builder.HasMany(g => g.Moves)
                .WithOne(m => m.Game)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Move>(builder =>
        {
            builder.HasKey(m => m.Id);

            builder.Property(m => m.Id)
                .ValueGeneratedOnAdd();

            builder.Property(m => m.San)
                .HasMaxLength(16)
                .IsRequired();

            builder.Property(m => m.FromSquare)
                .HasMaxLength(2)
                .IsRequired();

            builder.Property(m => m.ToSquare)
                .HasMaxLength(2)
                .IsRequired();

            builder.Property(m => m.ResultingFen)
                .HasMaxLength(100)
                .IsRequired();

            builder.HasIndex(m => new { m.GameId, m.MoveNumber })
                .IsUnique();
        });
    }
}
