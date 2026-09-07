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
    public DbSet<EmailConnection> EmailConnections => Set<EmailConnection>();
    public DbSet<EmailTicketMark> EmailTicketMarks => Set<EmailTicketMark>();
    public DbSet<TicketAssignee> TicketAssignees => Set<TicketAssignee>();
    public DbSet<OverdueNotificationSettings> OverdueNotificationSettings => Set<OverdueNotificationSettings>();
    public DbSet<SubBranch> SubBranches => Set<SubBranch>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<WorkTaskAssignmentLog> WorkTaskAssignmentLogs => Set<WorkTaskAssignmentLog>();
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

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
            e.HasIndex(u => u.DepartmentId);
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
            e.HasIndex(t => t.DepartmentId);
            e.HasIndex(t => t.SubBranchId);
            // Matches the default list sort (most urgent first, then soonest due date) so large ticket
            // volumes can be paged via an index scan instead of sorting the whole filtered set every request.
            e.HasIndex(t => new { t.Priority, t.DueDateUtc });

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
            e.HasIndex(m => m.TicketId);
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
            e.HasIndex(n => new { n.UserId, n.IsRead });
        });

        builder.Entity<KnowledgeBaseChunk>(e =>
        {
            e.HasOne(c => c.Document).WithMany(d => d.Chunks).HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Ticket).WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);

            // SQL Server has no native float-array column type (unlike Postgres' real[]), so the
            // embedding is stored as a JSON string instead. Similarity is already computed in C# after
            // loading matches into memory (see KnowledgeBaseController), never pushed down to SQL, so
            // this doesn't change any query semantics.
            var embeddingConverter = new ValueConverter<float[]?, string?>(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null));
            var embeddingComparer = new ValueComparer<float[]?>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, f) => HashCode.Combine(h, f)),
                v => v == null ? null : (float[])v.Clone());
            e.Property(c => c.Embedding).HasConversion(embeddingConverter, embeddingComparer);
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
            e.HasOne(a => a.Ticket).WithMany(t => t.Assignees).HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
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
            e.HasIndex(t => t.AssignedToUserId);
            e.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.AssignmentHistory).WithOne(h => h.WorkTask).HasForeignKey(h => h.WorkTaskId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkTaskAssignmentLog>(e =>
        {
            e.HasIndex(h => h.WorkTaskId);
        });
    }
}
