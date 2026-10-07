using Microsoft.EntityFrameworkCore;

namespace Noto.Server.Data;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OpRow> Ops => Set<OpRow>();
    public DbSet<CurrentRow> CurrentRows => Set<CurrentRow>();
    public DbSet<WorkspaceSync> WorkspaceSyncs => Set<WorkspaceSync>();
    public DbSet<ExportJob> Exports => Set<ExportJob>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
        });
        m.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.HasIndex(d => d.UserId);
        });
        m.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.DeviceId);
        });
        m.Entity<OpRow>(e =>
        {
            e.ToTable("ops");
            e.HasKey(o => o.Seq);
            e.Property(o => o.Seq).ValueGeneratedOnAdd();
            e.HasIndex(o => o.OpId).IsUnique();
            e.HasIndex(o => new { o.WorkspaceId, o.Seq });
            e.HasIndex(o => new { o.EntityType, o.EntityId });
        });
        m.Entity<CurrentRow>(e =>
        {
            e.ToTable("current_rows");
            e.HasKey(r => new { r.EntityType, r.EntityId });
            e.HasIndex(r => new
            {
                r.UserId,
                r.WorkspaceId,
                r.EntityType,
                r.EntityId,
            });
            e.HasIndex(r => r.DeletedAt);
        });
        m.Entity<WorkspaceSync>(e =>
        {
            e.ToTable("workspace_sync");
            e.HasKey(w => w.WorkspaceId);
            e.HasIndex(w => w.UserId);
        });
        m.Entity<ExportJob>().ToTable("exports");
    }
}
