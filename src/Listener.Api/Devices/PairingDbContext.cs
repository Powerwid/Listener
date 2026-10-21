using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Listener.Api.Devices;

public sealed class CollectorDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string InstallationId { get; set; }
    public required string NotificationDeviceId { get; set; }
    public required string Model { get; set; }
    public required string CredentialHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class DevicePairingTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TokenHash { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

// Isolated migration history: the payment schema and its existing migrations stay unchanged.
public sealed class PairingDbContext(DbContextOptions<PairingDbContext> options) : DbContext(options)
{
    public DbSet<CollectorDevice> Devices => Set<CollectorDevice>();
    public DbSet<DevicePairingTicket> Tickets => Set<DevicePairingTicket>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var device = model.Entity<CollectorDevice>();
        device.ToTable("ListenerCollectorDevices");
        device.HasKey(x => x.Id);
        device.Property(x => x.Id).ValueGeneratedNever();
        device.Property(x => x.InstallationId).HasMaxLength(32).IsUnicode(false);
        device.HasIndex(x => x.InstallationId).IsUnique();
        device.Property(x => x.NotificationDeviceId).HasMaxLength(100);
        device.Property(x => x.Model).HasMaxLength(120);
        device.Property(x => x.CredentialHash).HasMaxLength(64).IsUnicode(false);
        device.Property(x => x.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        device.Property(x => x.LinkedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        device.Property(x => x.LastSeenAt).HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        device.Property(x => x.RevokedAt).HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        var ticket = model.Entity<DevicePairingTicket>();
        ticket.ToTable("ListenerDevicePairingTickets");
        ticket.HasKey(x => x.Id);
        ticket.Property(x => x.Id).ValueGeneratedNever();
        ticket.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false);
        ticket.HasIndex(x => x.TokenHash).IsUnique();
        ticket.Property(x => x.CreatedBy).HasMaxLength(100);
        ticket.Property(x => x.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        ticket.Property(x => x.ExpiresAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        ticket.Property(x => x.ConsumedAt).HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        ticket.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class PairingDesignFactory : IDesignTimeDbContextFactory<PairingDbContext>
{
    public PairingDbContext CreateDbContext(string[] args)
    {
        Listener.Api.Configuration.LocalEnvironment.Load();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(typeof(PairingDbContext).Assembly.Location)!)
            .AddJsonFile("appsettings.json", optional: true).AddUserSecrets<Program>(optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("ListenerDb")
            ?? "Server=localhost;Database=ListenerDb;Integrated Security=True;TrustServerCertificate=True";
        return new(new DbContextOptionsBuilder<PairingDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__PairingMigrationsHistory")).Options);
    }
}
