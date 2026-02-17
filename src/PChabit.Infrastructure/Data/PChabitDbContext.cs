using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;

namespace PChabit.Infrastructure.Data;

public class PChabitDbContext : DbContext
{
    public DbSet<AppSession> AppSessions { get; set; }
    public DbSet<KeyboardSession> KeyboardSessions { get; set; }
    public DbSet<MouseSession> MouseSessions { get; set; }
    public DbSet<WebSession> WebSessions { get; set; }
    public DbSet<WorkflowSession> WorkflowSessions { get; set; }
    public DbSet<DailyPattern> DailyPatterns { get; set; }
    public DbSet<ProgramCategory> ProgramCategories { get; set; }
    public DbSet<ProgramCategoryMapping> ProgramCategoryMappings { get; set; }
    public DbSet<WebsiteCategory> WebsiteCategories { get; set; }
    public DbSet<WebsiteDomainMapping> WebsiteDomainMappings { get; set; }
    public DbSet<BackupRecord> BackupRecords { get; set; }
    public DbSet<ArchiveRecord> ArchiveRecords { get; set; }
    public DbSet<UserGoal> UserGoals { get; set; }
    public DbSet<EfficiencyScore> EfficiencyScores { get; set; }
    public DbSet<WorkPattern> WorkPatterns { get; set; }
    public DbSet<InsightReport> InsightReports { get; set; }
    
    public PChabitDbContext(DbContextOptions<PChabitDbContext> options) : base(options)
    {
    }
    
    public override int SaveChanges()
    {
        LogKeyboardChanges();
        return base.SaveChanges();
    }
    
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        LogKeyboardChanges();
        return await base.SaveChangesAsync(cancellationToken);
    }
    
    private void LogKeyboardChanges()
    {
        var keyboardEntries = ChangeTracker.Entries<KeyboardSession>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .ToList();
        
        foreach (var entry in keyboardEntries)
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property(e => e.KeyFrequency).IsModified = true;
                entry.Property(e => e.KeyCategoryFrequency).IsModified = true;
                entry.Property(e => e.Shortcuts).IsModified = true;
                entry.Property(e => e.TypingBursts).IsModified = true;
            }
            
            var session = entry.Entity;
            Log.Information("DbContext: 保存 KeyboardSession Date={Date}, Hour={Hour}, TotalKeyPresses={Total}, KeyFrequency={KeyFrequency}, State={State}",
                session.Date, session.Hour, session.TotalKeyPresses,
                System.Text.Json.JsonSerializer.Serialize(session.KeyFrequency),
                entry.State);
        }
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAppSession(modelBuilder);
        ConfigureKeyboardSession(modelBuilder);
        ConfigureMouseSession(modelBuilder);
        ConfigureWebSession(modelBuilder);
        ConfigureWorkflowSession(modelBuilder);
        ConfigureDailyPattern(modelBuilder);
        ConfigureProgramCategory(modelBuilder);
        ConfigureWebsiteCategory(modelBuilder);
        ConfigureBackupRecord(modelBuilder);
        ConfigureArchiveRecord(modelBuilder);
        ConfigureUserGoal(modelBuilder);
        ConfigureEfficiencyScore(modelBuilder);
        ConfigureWorkPattern(modelBuilder);
        ConfigureInsightReport(modelBuilder);
    }
    
    private static void ConfigureAppSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.ProcessName);
            entity.HasIndex(e => new { e.StartTime, e.ProcessName });
            
            entity.Property(e => e.Duration)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v))
                .HasField("_duration")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            entity.Property(e => e.ActiveDuration)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v))
                .HasField("_activeDuration")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
    
    private static void ConfigureKeyboardSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyboardSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => new { e.Date, e.Hour }).IsUnique();
            
            entity.Property(e => e.KeyFrequency).HasConversion(
                v => v == null || !v.Any() ? "{}" : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new Dictionary<int, int>() : System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<int, int>());
            
            entity.Property(e => e.KeyCategoryFrequency).HasConversion(
                v => v == null || !v.Any() ? "{}" : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new Dictionary<string, int>() : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, int>());
            
            entity.Property(e => e.Shortcuts).HasConversion(
                v => v == null || !v.Any() ? "[]" : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<ShortcutUsage>() : System.Text.Json.JsonSerializer.Deserialize<List<ShortcutUsage>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<ShortcutUsage>());
            
            entity.Property(e => e.TypingBursts).HasConversion(
                v => v == null || !v.Any() ? "[]" : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<TypingBurst>() : System.Text.Json.JsonSerializer.Deserialize<List<TypingBurst>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<TypingBurst>());
        });
    }
    
    private static void ConfigureMouseSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MouseSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => new { e.Date, e.Hour }).IsUnique();
            
            entity.Ignore(e => e.ClickByArea);
            entity.Ignore(e => e.ScrollByDirection);
            entity.Ignore(e => e.Trails);
            entity.Ignore(e => e.ClickClusters);
            entity.Ignore(e => e.TotalClicks);
            entity.Ignore(e => e.TotalDistance);
        });
    }
    
    private static void ConfigureWebSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.Domain);
            
            entity.Property(e => e.Duration)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v))
                .HasField("_duration")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            entity.Property(e => e.ActiveDuration)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v))
                .HasField("_activeDuration")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            entity.Ignore(e => e.InteractedElements);
            entity.Ignore(e => e.SearchQueries);
            entity.Ignore(e => e.TabSwitches);
        });
    }
    
    private static void ConfigureWorkflowSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.StartTime);
            
            entity.Ignore(e => e.ActiveApplications);
            entity.Ignore(e => e.AppTimeDistribution);
            entity.Ignore(e => e.AverageFocusDuration);
            entity.Ignore(e => e.EditedFilePaths);
            entity.Ignore(e => e.ContextSwitches);
        });
    }
    
    private static void ConfigureDailyPattern(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyPattern>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.Date).IsUnique();
            
            entity.Ignore(e => e.FocusBlocks);
            entity.Ignore(e => e.Breaks);
            entity.Ignore(e => e.HourlyActivity);
            entity.Ignore(e => e.Patterns);
        });
    }
    
    private static void ConfigureProgramCategory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProgramCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.SortOrder);
        });
        
        modelBuilder.Entity<ProgramCategoryMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProcessName);
            entity.HasIndex(e => new { e.ProcessName, e.CategoryId }).IsUnique();
            
            entity.HasOne(e => e.Category)
                .WithMany(c => c.ProgramMappings)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureWebsiteCategory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebsiteCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.SortOrder);
        });

        modelBuilder.Entity<WebsiteDomainMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DomainPattern);
            entity.HasIndex(e => new { e.DomainPattern, e.CategoryId }).IsUnique();

            entity.HasOne(e => e.Category)
                .WithMany(c => c.DomainMappings)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureBackupRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackupRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureArchiveRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArchiveRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.DateRangeStart);
        });
    }

    private static void ConfigureUserGoal(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserGoal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.TargetType);
            entity.HasIndex(e => e.IsActive);
        });
    }

    private static void ConfigureEfficiencyScore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfficiencyScore>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.Date).IsUnique();
        });
    }

    private static void ConfigureWorkPattern(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkPattern>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.Date).IsUnique();
        });
    }

    private static void ConfigureInsightReport(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InsightReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v));
            entity.HasIndex(e => e.ReportType);
            entity.HasIndex(e => e.StartDate);
        });
    }
}
