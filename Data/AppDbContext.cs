using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using КР_Ханников.Core;

namespace КР_Ханников.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<TicketHistory> TicketHistories { get; set; }
        public DbSet<Solution> Solutions { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<TicketComment> TicketComments { get; set; }
        public DbSet<KnowledgeArticle> KnowledgeBase { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<NotificationSettings> NotificationSettings { get; set; }
        public DbSet<SearchPreset> SearchPresets { get; set; }
        public DbSet<UserUiSettings> UserUiSettings { get; set; }
        public DbSet<TicketAttachment> TicketAttachments { get; set; }
        public DbSet<MlModelMetrics> MlModelMetrics { get; set; }

        static AppDbContext()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        public AppDbContext() { }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = Constants.Database.GetConnectionString();

                optionsBuilder
                    .UseNpgsql(connectionString)
                    .LogTo(message => Debug.WriteLine($"[EF Core] {message}"),
                        new[] { DbLoggerCategory.Database.Command.Name },
                        LogLevel.Information);

#if DEBUG
                optionsBuilder.EnableSensitiveDataLogging().EnableDetailedErrors();
#endif
            }
        }

                protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            configurationBuilder
                .Properties<DateTime>()
                .HaveConversion<UtcDateTimeConverter>();

            configurationBuilder
                .Properties<DateTime?>()
                .HaveConversion<NullableUtcDateTimeConverter>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(entity.DisplayName());
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.Name);
                }
            }

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Username).IsRequired().HasMaxLength(Constants.Validation.MaxUsernameLength);
                entity.HasIndex(u => u.Username).IsUnique();
                entity.Property(u => u.PasswordHash).IsRequired();
                entity.Property(u => u.Role).IsRequired().HasDefaultValue(Constants.UserRoles.Client);
                entity.Property(u => u.AvatarPath).HasMaxLength(500);
                entity.Property(u => u.Email).HasMaxLength(100);
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(u => u.FailedLoginAttempts).HasDefaultValue(0);
                entity.Property(u => u.MustChangePassword).HasDefaultValue(false);
                entity.Ignore(u => u.Employee);
                entity.Ignore(u => u.Client);
            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(Constants.Validation.MaxEmployeeNameLength);
                entity.Property(e => e.Role).IsRequired();
                entity.HasOne(e => e.User).WithOne().HasForeignKey<Employee>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Name).IsRequired().HasMaxLength(Constants.Validation.MaxEmployeeNameLength);
                entity.HasOne(c => c.User).WithOne().HasForeignKey<Client>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Ticket>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Title).IsRequired().HasMaxLength(Constants.Validation.MaxTicketTitleLength);
                entity.Property(t => t.Status).HasDefaultValue(Constants.TicketStatus.Open);
                entity.Property(t => t.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(t => t.Priority).HasConversion<int>();
                entity.Property(t => t.Category).HasConversion<int>();

                entity.HasOne(t => t.Client).WithMany(c => c.Tickets).HasForeignKey(t => t.ClientId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeEmployeeId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(t => t.Solution).WithOne(s => s.Ticket).HasForeignKey<Solution>(s => s.TicketId);
            });

            modelBuilder.Entity<Solution>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.ResolutionText).IsRequired();
                entity.Property(s => s.ResolutionDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(s => s.Ticket).WithOne(t => t.Solution).HasForeignKey<Solution>(s => s.TicketId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TicketHistory>(entity =>
            {
                entity.HasKey(h => h.Id);
                entity.Property(h => h.Action).IsRequired().HasMaxLength(100);
                entity.Property(h => h.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne<Ticket>().WithMany(t => t.History).HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Feedback>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.Property(f => f.Comment).IsRequired();
                entity.Property(f => f.Rating).IsRequired();
                entity.Property(f => f.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(f => f.Ticket).WithOne(t => t.Feedback).HasForeignKey<Feedback>(f => f.TicketId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(f => f.Client).WithMany().HasForeignKey(f => f.ClientId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(f => f.Support).WithMany().HasForeignKey(f => f.SupportId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<KnowledgeArticle>(entity =>
            {
                entity.HasKey(k => k.Id);
                entity.Property(k => k.Title).IsRequired().HasMaxLength(200);
                entity.Property(k => k.Content).IsRequired();
                entity.Property(k => k.ViewCount).HasDefaultValue(0);
                entity.Property(k => k.HelpfulCount).HasDefaultValue(0);
                entity.Property(k => k.NotHelpfulCount).HasDefaultValue(0);
                entity.Property(k => k.IsPublished).HasDefaultValue(true);
            });

            modelBuilder.Entity<MlModelMetrics>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.ModelType).IsRequired().HasMaxLength(50);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Username).IsRequired().HasMaxLength(50);
                entity.Property(a => a.Action).IsRequired().HasMaxLength(100);
                entity.Property(a => a.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<TicketComment>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Text).IsRequired();
                entity.Property(c => c.IsInternal).HasDefaultValue(true);
                entity.Property(c => c.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(c => c.Ticket)
                      .WithMany(t => t.Comments)
                      .HasForeignKey(c => c.TicketId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(c => c.Author)
                      .WithMany()
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(n => n.Id);
                entity.Property(n => n.Title).IsRequired().HasMaxLength(200);
                entity.Property(n => n.Message).IsRequired().HasMaxLength(2000);
                entity.Property(n => n.Type).IsRequired().HasMaxLength(50);
                entity.Property(n => n.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(n => n.Ticket).WithMany().HasForeignKey(n => n.TicketId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<NotificationSettings>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.HasIndex(s => s.UserId).IsUnique();
                entity.Property(s => s.NotifyOnComments).HasDefaultValue(true);
                entity.Property(s => s.PlaySound).HasDefaultValue(true);
                entity.Property(s => s.ShowToast).HasDefaultValue(true);
                entity.HasOne(s => s.User).WithOne().HasForeignKey<NotificationSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SearchPreset>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
                entity.Property(x => x.TextQuery).HasMaxLength(500);
                entity.Property(x => x.Status).HasMaxLength(50);
                entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TicketAttachment>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.FileName).IsRequired().HasMaxLength(255);
                entity.Property(a => a.StoredFilePath).IsRequired().HasMaxLength(500);
                entity.Property(a => a.ContentType).HasMaxLength(100);
                entity.HasOne(a => a.Ticket).WithMany(t => t.Attachments).HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(a => a.UploadedByUser).WithMany().HasForeignKey(a => a.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Ticket>(entity =>
            {
                entity.Property(t => t.IsOverdue).HasDefaultValue(false);
                entity.Property(t => t.NotifiedAboutDeadline).HasDefaultValue(false);
            });

            modelBuilder.Entity<UserUiSettings>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => x.UserId).IsUnique();
                entity.Property(x => x.ShowKpiBlock).HasDefaultValue(true);
                entity.Property(x => x.ShowChartsBlock).HasDefaultValue(true);
                entity.Property(x => x.ShowDetailedTable).HasDefaultValue(true);
                entity.Property(x => x.DefaultPeriodDays).HasDefaultValue(30);
                entity.Property(x => x.RefreshRateSeconds).HasDefaultValue(30);
                entity.Property(x => x.Theme).HasMaxLength(20).HasDefaultValue("Light");
                entity.Ignore(x => x.ShowKpi);
                entity.Ignore(x => x.ShowCharts);
                entity.Ignore(x => x.ShowTable);
                entity.Ignore(x => x.RefreshIntervalSeconds);
                entity.Ignore(x => x.AutoRefresh);
                entity.HasOne(x => x.User).WithOne().HasForeignKey<UserUiSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }

        public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => v == DateTime.MinValue ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        { }
    }

    public class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter() : base(
            v => v.HasValue ? (v.Value == DateTime.MinValue ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        { }
    }
}