using Microsoft.EntityFrameworkCore;
using Prediction.Entities;

namespace Prediction.Data;

public sealed class FootballDbContext : DbContext
{
    public FootballDbContext(DbContextOptions<FootballDbContext> options)
        : base(options)
    {
    }

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<PredictionSnapshot> PredictionSnapshots
    => Set<PredictionSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Match>(entity =>
        {
            entity.ToTable("matches");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.ExternalMatchId)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.HomeTeam)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.AwayTeam)
                .HasMaxLength(200)
                .IsRequired();

            entity.HasIndex(x => x.ExternalMatchId)
                .IsUnique();

            entity.HasIndex(x => x.KickoffUtc);
        });

        modelBuilder.Entity<PredictionSnapshot>(entity =>
        {
            entity.ToTable("prediction_snapshots");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Source)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.PredictedResult)
                .HasMaxLength(20);

            entity.Property(x => x.PredictedScore)
                .HasMaxLength(20);

            entity.Property(x => x.Btts)
                .HasMaxLength(20);

            entity.Property(x => x.Goals)
                .HasMaxLength(50);

            entity.HasOne(x => x.Match)
                .WithMany(x => x.PredictionSnapshots)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.MatchId);

            entity.HasIndex(x => new
            {
                x.MatchId,
                x.Source,
                x.CapturedAtUtc
            });
        });
    }
}