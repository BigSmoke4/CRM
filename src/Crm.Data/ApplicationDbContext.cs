using Crm.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext? _tenant;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenant = null)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<ContactNote> ContactNotes => Set<ContactNote>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<DealStage> DealStages => Set<DealStage>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealProduct> DealProducts => Set<DealProduct>();

    public DbSet<IntegrationConfig> IntegrationConfigs => Set<IntegrationConfig>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<SyncRecordError> SyncRecordErrors => Set<SyncRecordError>();

    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<PaymentProviderConfig> PaymentProviderConfigs => Set<PaymentProviderConfig>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<AiAssistant> AiAssistants => Set<AiAssistant>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketReply> TicketReplies => Set<TicketReply>();

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---------- Multi-tenant global query filters ----------
        ApplyTenantFilter<Contact>(builder);
        ApplyTenantFilter<Account>(builder);
        ApplyTenantFilter<Lead>(builder);
        ApplyTenantFilter<ContactNote>(builder);
        ApplyTenantFilter<Activity>(builder);
        ApplyTenantFilter<Attachment>(builder);
        ApplyTenantFilter<CustomFieldDefinition>(builder);
        ApplyTenantFilter<AuditLog>(builder);
        ApplyTenantFilter<Notification>(builder);
        ApplyTenantFilter<Team>(builder);
        ApplyTenantFilter<Pipeline>(builder);
        ApplyTenantFilter<DealStage>(builder);
        ApplyTenantFilter<Deal>(builder);
        ApplyTenantFilter<IntegrationConfig>(builder);
        ApplyTenantFilter<SyncLog>(builder);
        ApplyTenantFilter<SyncRecordError>(builder);
        ApplyTenantFilter<AiProviderConfig>(builder);
        ApplyTenantFilter<KnowledgeDocument>(builder);
        ApplyTenantFilter<KnowledgeChunk>(builder);
        ApplyTenantFilter<AiAssistant>(builder);
        ApplyTenantFilter<AiConversation>(builder);
        ApplyTenantFilter<AiMessage>(builder);
        ApplyTenantFilter<Campaign>(builder);
        ApplyTenantFilter<CampaignRecipient>(builder);
        ApplyTenantFilter<Ticket>(builder);
        ApplyTenantFilter<TicketReply>(builder);
        ApplyTenantFilter<WorkflowDefinition>(builder);
        ApplyTenantFilter<WorkflowRun>(builder);
        ApplyTenantFilter<Product>(builder);
        ApplyTenantFilter<Order>(builder);
        ApplyTenantFilter<OrderLine>(builder);
        ApplyTenantFilter<Invoice>(builder);
        ApplyTenantFilter<InvoiceLine>(builder);
        ApplyTenantFilter<DealProduct>(builder);

        // DealProduct / OrderLine / InvoiceLine inherit tenancy via parent; filter through nav is complex,
        // so they get explicit OrganizationId-free design: they are only reachable through tenant-filtered parents.
        // To be safe we still scope them by joining parent org — simplified: they carry no OrganizationId,
        // access is always via parent queries.

        // ---------- Indexes ----------
        builder.Entity<Contact>().HasIndex(c => new { c.OrganizationId, c.Email });
        builder.Entity<Contact>().HasIndex(c => new { c.OrganizationId, c.LastName, c.FirstName });
        builder.Entity<Contact>().HasIndex(c => new { c.OrganizationId, c.AccountId });
        builder.Entity<Contact>().HasIndex(c => new { c.OrganizationId, c.AssignedToId });
        builder.Entity<Contact>().HasIndex(c => new { c.OrganizationId, c.ExternalRefId });
        builder.Entity<Contact>().HasIndex(c => c.IsDeleted);

        builder.Entity<Account>().HasIndex(a => new { a.OrganizationId, a.Name });
        builder.Entity<Account>().HasIndex(a => new { a.OrganizationId, a.ExternalRefId });

        builder.Entity<Lead>().HasIndex(l => new { l.OrganizationId, l.Status });
        builder.Entity<Lead>().HasIndex(l => new { l.OrganizationId, l.Score });
        builder.Entity<Lead>().HasIndex(l => new { l.OrganizationId, l.Email });
        builder.Entity<Lead>().HasIndex(l => new { l.OrganizationId, l.ExternalRefId });

        builder.Entity<Activity>().HasIndex(a => new { a.OrganizationId, a.ContactId, a.CreatedAt });
        builder.Entity<Activity>().HasIndex(a => new { a.OrganizationId, a.Type, a.CreatedAt });
        builder.Entity<Activity>().HasIndex(a => new { a.OrganizationId, a.DueAt });

        builder.Entity<Deal>().HasIndex(d => new { d.OrganizationId, d.StageId });
        builder.Entity<Deal>().HasIndex(d => new { d.OrganizationId, d.OwnerId });
        builder.Entity<Deal>().HasIndex(d => new { d.OrganizationId, d.ExpectedCloseDate });
        builder.Entity<Deal>().HasIndex(d => d.IsDeleted);

        builder.Entity<Ticket>().HasIndex(t => new { t.OrganizationId, t.Status });
        builder.Entity<Ticket>().HasIndex(t => new { t.OrganizationId, t.TicketNumber }).IsUnique();
        builder.Entity<Ticket>().HasIndex(t => new { t.OrganizationId, t.SlaDueAt });

        builder.Entity<Order>().HasIndex(o => new { o.OrganizationId, o.OrderNumber }).IsUnique();
        builder.Entity<Invoice>().HasIndex(i => new { i.OrganizationId, i.InvoiceNumber }).IsUnique();
        builder.Entity<Invoice>().HasIndex(i => new { i.OrganizationId, i.Status, i.DueDate });

        builder.Entity<Product>().HasIndex(p => new { p.OrganizationId, p.Sku });

        builder.Entity<IntegrationConfig>().HasIndex(i => new { i.OrganizationId, i.IsEnabled });
        builder.Entity<SyncLog>().HasIndex(s => new { s.OrganizationId, s.IntegrationId, s.StartedAt });

        builder.Entity<KnowledgeChunk>().HasIndex(k => new { k.OrganizationId, k.DocumentId });
        builder.Entity<AiConversation>().HasIndex(c => new { c.OrganizationId, c.UserId });

        builder.Entity<Campaign>().HasIndex(c => new { c.OrganizationId, c.Status });
        builder.Entity<CampaignRecipient>().HasIndex(r => new { r.OrganizationId, r.CampaignId });

        builder.Entity<AuditLog>().HasIndex(a => new { a.OrganizationId, a.CreatedAt });
        builder.Entity<AuditLog>().HasIndex(a => new { a.OrganizationId, a.EntityType, a.EntityId });
        builder.Entity<Notification>().HasIndex(n => new { n.OrganizationId, n.UserId, n.IsRead });

        // ---------- Decimal precision ----------
        builder.Entity<Deal>().Property(d => d.Amount).HasPrecision(18, 2);
        builder.Entity<Account>().Property(a => a.AnnualRevenue).HasPrecision(18, 2);
        builder.Entity<Account>().Property(a => a.LifetimeValue).HasPrecision(18, 2);
        builder.Entity<DealProduct>().Property(p => p.UnitPrice).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.Cost).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Subtotal).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Discount).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Tax).HasPrecision(18, 2);
        builder.Entity<Order>().Property(o => o.Total).HasPrecision(18, 2);
        builder.Entity<Invoice>().Property(i => i.Tax).HasPrecision(18, 2);
        builder.Entity<OrderLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Entity<OrderLine>().Property(l => l.LineTotal).HasPrecision(18, 2);
        builder.Entity<Invoice>().Property(i => i.Subtotal).HasPrecision(18, 2);
        builder.Entity<Invoice>().Property(i => i.Total).HasPrecision(18, 2);
        builder.Entity<Invoice>().Property(i => i.AmountPaid).HasPrecision(18, 2);
        builder.Entity<InvoiceLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Entity<InvoiceLine>().Property(l => l.LineTotal).HasPrecision(18, 2);

        // ---------- String lengths ----------
        builder.Entity<Contact>().Property(c => c.Email).HasMaxLength(256);
        builder.Entity<Contact>().Property(c => c.ExternalRefId).HasMaxLength(200);
        builder.Entity<Account>().Property(a => a.ExternalRefId).HasMaxLength(200);
        builder.Entity<Lead>().Property(l => l.ExternalRefId).HasMaxLength(200);
        builder.Entity<IntegrationConfig>().Property(i => i.BaseUrl).HasMaxLength(2000);
        builder.Entity<IntegrationConfig>().Property(i => i.Endpoint).HasMaxLength(2000);
        builder.Entity<Ticket>().Property(t => t.TicketNumber).HasMaxLength(50);
        builder.Entity<Order>().Property(o => o.OrderNumber).HasMaxLength(50);
        builder.Entity<Invoice>().Property(i => i.InvoiceNumber).HasMaxLength(50);

        // ---------- JSON columns (SQL Server) ----------
        builder.Entity<Contact>().Property(c => c.CustomFieldsJson).HasColumnType("nvarchar(max)");
        builder.Entity<IntegrationConfig>().Property(i => i.MappingRulesJson).HasColumnType("nvarchar(max)");
        builder.Entity<IntegrationConfig>().Property(i => i.RequestBody).HasColumnType("nvarchar(max)");
        builder.Entity<KnowledgeChunk>().Property(k => k.EmbeddingJson).HasColumnType("nvarchar(max)");
        builder.Entity<KnowledgeChunk>().Property(k => k.Content).HasColumnType("nvarchar(max)");
        builder.Entity<AiMessage>().Property(m => m.Content).HasColumnType("nvarchar(max)");

        // Restrict delete behavior to avoid cycles
        builder.Entity<Contact>().HasOne(c => c.Account).WithMany(a => a.Contacts)
            .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Contact>().HasOne(c => c.AssignedTo).WithMany()
            .HasForeignKey(c => c.AssignedToId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Deal>().HasOne(d => d.Account).WithMany()
            .HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Deal>().HasOne(d => d.Contact).WithMany()
            .HasForeignKey(d => d.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Deal>().HasOne(d => d.Owner).WithMany()
            .HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Deal>().HasOne(d => d.Stage).WithMany()
            .HasForeignKey(d => d.StageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Deal>().HasOne(d => d.Pipeline).WithMany(p => p.Deals)
            .HasForeignKey(d => d.PipelineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DealStage>().HasOne(s => s.Pipeline).WithMany(p => p.Stages)
            .HasForeignKey(s => s.PipelineId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Ticket>().HasOne(t => t.Contact).WithMany()
            .HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Ticket>().HasOne(t => t.Assignee).WithMany()
            .HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<TicketReply>().HasOne(r => r.Ticket).WithMany(t => t.Replies)
            .HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CampaignRecipient>().HasOne(r => r.Campaign).WithMany()
            .HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Order>().HasOne(o => o.Contact).WithMany()
            .HasForeignKey(o => o.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasOne(i => i.Order).WithMany()
            .HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<AiAssistant>().HasOne(a => a.Provider).WithMany()
            .HasForeignKey(a => a.ProviderId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ApplicationUser>().HasOne(u => u.Organization).WithMany(o => o.Users)
            .HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantEntity
    {
        builder.Entity<TEntity>().HasQueryFilter(e =>
            _tenant == null ||
            !_tenant.IsAuthenticated ||
            _tenant.IsSuperAdmin ||
            e.OrganizationId == _tenant.OrganizationId);
    }
}
