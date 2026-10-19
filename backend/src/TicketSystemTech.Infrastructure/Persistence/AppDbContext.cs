using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Infrastructure.Persistence;

// IDataProtectionKeyContext lets the Data Protection key ring live in this same database instead of
// the container's local disk — Render's filesystem doesn't survive a redeploy, which would otherwise
// make every previously-encrypted OAuth token unreadable the next time the app starts.
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    // IPiiProtector must be a singleton (see PiiProtector's own doc comment) — EF Core builds and
    // caches the model, including these converters, once for the app's lifetime, not per-request.
    private readonly IPiiProtector _piiProtector;

    public AppDbContext(DbContextOptions<AppDbContext> options, IPiiProtector piiProtector) : base(options)
    {
        _piiProtector = piiProtector;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<HelpTopic> HelpTopics => Set<HelpTopic>();
    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
    public DbSet<SlaPlan> SlaPlans => Set<SlaPlan>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<KnowledgeBaseDocument> KnowledgeBaseDocuments => Set<KnowledgeBaseDocument>();
    public DbSet<KnowledgeBaseChunk> KnowledgeBaseChunks => Set<KnowledgeBaseChunk>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<EmailConnection> EmailConnections => Set<EmailConnection>();
    public DbSet<EmailTicketMark> EmailTicketMarks => Set<EmailTicketMark>();
    public DbSet<TicketAssignee> TicketAssignees => Set<TicketAssignee>();
    public DbSet<OverdueNotificationSettings> OverdueNotificationSettings => Set<OverdueNotificationSettings>();
    public DbSet<SubBranch> SubBranches => Set<SubBranch>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<WorkTaskAssignmentLog> WorkTaskAssignmentLogs => Set<WorkTaskAssignmentLog>();
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Applies to every DateTime / DateTime? property in the model, so values leave the API as real UTC instants.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasSequence<long>("TicketNumberSequence").StartsAt(100001).IncrementsBy(1);

        // Encrypts personal data at rest. NormalizedEmail/NormalizedUserName are NOT run through this —
        // they hold a one-way hash instead (see PiiLookupNormalizer), so login lookups stay exact-match
        // without ever storing the plaintext email in a second, unencrypted column.
        var piiConverter = new ValueConverter<string?, string?>(
            v => _piiProtector.Encrypt(v),
            v => _piiProtector.Decrypt(v));

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Email).HasConversion(piiConverter).HasMaxLength(1000);
            e.Property(u => u.UserName).HasConversion(piiConverter).HasMaxLength(1000);
            e.Property(u => u.PhoneNumber).HasConversion(piiConverter);
            e.HasIndex(u => u.CompanyId);
            e.HasIndex(u => u.Role);
            // "Active employees of branch X" is the fan-out behind every new-ticket and transfer notification.
            e.HasIndex(u => new { u.DepartmentId, u.Role, u.IsActive });
            e.HasIndex(u => u.SubBranchId);
            e.HasOne<Department>().WithMany().HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<SubBranch>().WithMany().HasForeignKey(u => u.SubBranchId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Company>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Address).HasConversion(piiConverter);
        });

        builder.Entity<Department>(e =>
        {
            e.Property(d => d.Name).HasMaxLength(150).IsRequired();
            e.Property(d => d.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(d => d.Email).IsUnique();
        });

        builder.Entity<HelpTopic>(e =>
        {
            e.Property(h => h.Name).HasMaxLength(150).IsRequired();
        });

        builder.Entity<TicketCategory>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(150).IsRequired();
        });

        builder.Entity<SlaPlan>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(150).IsRequired();
        });

        builder.Entity<Ticket>(e =>
        {
            e.Property(t => t.TicketNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(t => t.TicketNumber).IsUnique();
            e.Property(t => t.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.CompanyId);
            e.HasIndex(t => t.AssignedToUserId);
            e.HasIndex(t => t.ClientId);
            e.HasIndex(t => t.CreatedAt);
            e.HasIndex(t => t.SubBranchId);
            // The default list sort is "most urgent first, then newest" (see TicketsController.List); the index
            // used to end in DueDateUtc, which the query never sorts by, so it could not be used for paging.
            e.HasIndex(t => new { t.Priority, t.CreatedAt }).IsDescending(false, true);
            // Staff lists, counters and every report are scoped to a branch and then filter on status or a date range.
            // The composites replace the single-column DepartmentId index (their leading column covers it).
            e.HasIndex(t => new { t.DepartmentId, t.Status });
            e.HasIndex(t => new { t.DepartmentId, t.CreatedAt });
            // The overdue job only ever looks at tickets that have not been notified yet; the filtered index
            // contains just those rows, so it stays tiny while the table grows.
            // (Named at creation: EF treats a second index on the same column as the first one otherwise.)
            e.HasIndex(t => t.Status, "IX_Tickets_Status_NotYetNotified")
                .HasFilter("[OverdueNotifiedAtUtc] IS NULL")
                .IncludeProperties(t => new { t.DueDateUtc, t.OpenedAtUtc });

            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.HelpTopic).WithMany().HasForeignKey(t => t.HelpTopicId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.SubBranch).WithMany().HasForeignKey(t => t.SubBranchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.SlaPlan).WithMany().HasForeignKey(t => t.SlaPlanId).OnDelete(DeleteBehavior.SetNull);

            e.HasMany(t => t.Messages).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Attachments).WithOne(a => a.Ticket).HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TicketMessage>(e =>
        {
            // "Has an internal note", "sum of internal note lengths" and the activity feed all start from a ticket and
            // a message type; including author and time lets them be answered from the index alone.
            e.HasIndex(m => new { m.TicketId, m.Type }).IncludeProperties(m => new { m.AuthorId, m.CreatedAt });
        });

        builder.Entity<TicketAttachment>(e =>
        {
            // NoAction: TicketAttachments.TicketId already cascades from Tickets directly (covers every
            // attachment, message-tied or not — MessageId is nullable for attachments on the original
            // submission). SQL Server refuses a second cascade path to the same table via TicketMessages,
            // and there's no codepath that deletes a single message without deleting its ticket anyway.
            e.HasOne(a => a.Message).WithMany(m => m.Attachments).HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Notification>(e =>
        {
            // The bell lists a user's notifications newest first and counts the unread ones.
            e.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true).IncludeProperties(n => new { n.IsRead, n.Type, n.TicketId });
        });

        builder.Entity<KnowledgeBaseChunk>(e =>
        {
            e.HasOne(c => c.Document).WithMany(d => d.Chunks).HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Ticket).WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
            // The indexer assumes one chunk per ticket. Two overlapping index runs (close + a note, or a manual reindex)
            // could each insert one; the unique index makes the second fail instead of leaving a duplicate.
            e.HasIndex(c => c.TicketId).IsUnique().HasFilter("[TicketId] IS NOT NULL");

            // SQL Server has no native float-array column type, so the vector is stored as the raw bytes of its
            // float32 values (varbinary): 12 KB for a 3072-number vector instead of ~78 KB of JSON text, and no
            // per-number parsing on load. Similarity is computed in C#, never pushed down to SQL.
            // The pre-binary JSON lives on in the legacy EmbeddingJson column, which is deliberately NOT part of
            // the model: EmbeddingFormatBackfill converts it, and a later migration drops it once that is verified.
            var embeddingConverter = new ValueConverter<float[]?, byte[]?>(
                v => v == null ? null : EmbeddingCodec.Encode(v),
                v => v == null ? null : EmbeddingCodec.Decode(v));
            var embeddingComparer = new ValueComparer<float[]?>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, f) => HashCode.Combine(h, f)),
                v => v == null ? null : (float[])v.Clone());
            e.Property(c => c.Embedding).HasColumnName("EmbeddingBinary").HasConversion(embeddingConverter, embeddingComparer);
            e.Property(c => c.EmbeddingModel).HasMaxLength(100);
        });

        builder.Entity<EmailConnection>(e =>
        {
            e.HasIndex(c => c.DepartmentId).IsUnique();
        });

        builder.Entity<EmailTicketMark>(e =>
        {
            e.HasIndex(m => new { m.DepartmentId, m.MessageId }).IsUnique();
        });

        builder.Entity<TicketAssignee>(e =>
        {
            e.HasIndex(a => new { a.TicketId, a.UserId }).IsUnique();
            // "My tickets", the Gantt and the timeline export start from a person, not from a ticket.
            e.HasIndex(a => new { a.UserId, a.TicketId });
            e.HasOne(a => a.Ticket).WithMany(t => t.Assignees).HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdempotencyRecord>(e =>
        {
            e.Property(r => r.Scope).HasMaxLength(50).IsRequired();
            e.Property(r => r.Key).HasMaxLength(64).IsRequired();
            // The uniqueness is what makes two simultaneous identical requests safe: the second insert fails.
            e.HasIndex(r => new { r.UserId, r.Scope, r.Key }).IsUnique();
        });

        builder.Entity<SubBranch>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(s => new { s.DepartmentId, s.Name }).IsUnique();
            // NoAction rather than Cascade: SQL Server refuses to create the AspNetUsers.SubBranchId ->
            // SubBranches (SetNull) constraint if this path could also cascade-delete through Departments
            // (multiple cascade paths reaching AspNetUsers). There's no sub-branch deletion endpoint
            // anyway — DeleteDepartment blocks while sub-branches still exist instead (see LookupsController).
            e.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<WorkTask>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(t => t.DepartmentId);
            // The Gantt asks for one person's tasks on one day.
            e.HasIndex(t => new { t.AssignedToUserId, t.StartedAtUtc }).IncludeProperties(t => new { t.EndedAtUtc, t.Status });
            e.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.AssignmentHistory).WithOne(h => h.WorkTask).HasForeignKey(h => h.WorkTaskId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkTaskAssignmentLog>(e =>
        {
            e.HasIndex(h => h.WorkTaskId);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired(); // indexed below, so it cannot stay nvarchar(max)
            e.HasIndex(a => new { a.EntityType, a.EntityId, a.CreatedAt });
            e.HasIndex(a => new { a.UserId, a.CreatedAt });
        });

        ConfigureReferences(builder);
    }

    /// <summary>
    /// Real foreign keys for the references that used to be bare Guid columns. Nothing stopped a typo, a bad import
    /// or a manual delete from leaving a ticket whose author, assignee or client no longer existed (the UI then showed
    /// an empty name). All are Restrict: accounts are only ever deactivated, never deleted, and a delete that would
    /// orphan history should fail loudly rather than cascade or null it out.
    /// </summary>
    private static void ConfigureReferences(ModelBuilder builder)
    {
        void ToUser<T>(System.Linq.Expressions.Expression<Func<T, object?>> key) where T : class =>
            builder.Entity<T>().HasOne<ApplicationUser>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);

        ToUser<Ticket>(t => t.ClientId);
        ToUser<Ticket>(t => t.AssignedToUserId);
        ToUser<Ticket>(t => t.OpenedByUserId);
        ToUser<Ticket>(t => t.ClosedByUserId);
        ToUser<TicketMessage>(m => m.AuthorId);
        ToUser<TicketAttachment>(a => a.UploadedByUserId);
        ToUser<TicketAssignee>(a => a.UserId);
        ToUser<Notification>(n => n.UserId);
        ToUser<KnowledgeBaseDocument>(d => d.UploadedByUserId);
        ToUser<WorkTask>(t => t.CreatedByUserId);
        ToUser<WorkTask>(t => t.AssignedByUserId);
        ToUser<WorkTask>(t => t.AssignedToUserId);
        ToUser<WorkTaskAssignmentLog>(h => h.AssignedByUserId);
        ToUser<WorkTaskAssignmentLog>(h => h.AssignedToUserId);
        ToUser<EmailConnection>(c => c.ConnectedByUserId);
        ToUser<EmailTicketMark>(m => m.MarkedByUserId);
        ToUser<AuditLog>(a => a.UserId);

        builder.Entity<ApplicationUser>().HasOne<Company>().WithMany().HasForeignKey(u => u.CompanyId).OnDelete(DeleteBehavior.Restrict);

        // The mailbox tables pointed at a branch without a constraint, so deleting a branch left its OAuth tokens behind.
        builder.Entity<EmailConnection>().HasOne<Department>().WithMany().HasForeignKey(c => c.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmailTicketMark>().HasOne<Department>().WithMany().HasForeignKey(m => m.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmailTicketMark>().HasOne<Ticket>().WithMany().HasForeignKey(m => m.ConvertedTicketId).OnDelete(DeleteBehavior.Restrict);
    }
}
