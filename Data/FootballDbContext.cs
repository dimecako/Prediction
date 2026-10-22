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

    public DbSet<SimulatedTicket> SimulatedTickets => Set<SimulatedTicket>();

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

        modelBuilder.Entity<SimulatedTicket>(entity =>
        {
            entity.ToTable("simulated_tickets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Mode).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Fingerprint).IsUnique();
            entity.HasIndex(x => x.MatchDate);
        });

        modelBuilder.Entity<SimulatedTicketSelection>(entity =>
        {
            entity.ToTable("simulated_ticket_selections");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Ticket).WithMany(x => x.Selections)
                .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Match).WithMany()
                .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TicketId, x.MatchId }).IsUnique();
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

        modelBuilder.Entity<MatchResult>(entity =>
        {
            entity.ToTable("match_results");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Result)
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(x => x.Source)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasOne(x => x.Match)
                .WithOne(x => x.Result)
                .HasForeignKey<MatchResult>(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.MatchId)
                .IsUnique();
        });
    }
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
}

public sealed class FootballDbContextFactory :
    Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<FootballDbContext>
{
    public FootballDbContext CreateDbContext(string[] args)
    {
        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__FootballDb is not configured.");

        return new FootballDbContext(new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql(connection).Options);
    }
}