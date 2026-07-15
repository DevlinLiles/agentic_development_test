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

            builder.Property(g => g.CurrentFen)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(g => g.IsVsAi)
                .HasDefaultValue(false);

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
