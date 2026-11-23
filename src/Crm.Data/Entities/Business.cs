namespace Crm.Data.Entities;

// ---------- Marketing ----------

public enum CampaignStatus { Draft, Scheduled, Running, Paused, Completed, Cancelled }

public class Campaign : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public string Channel { get; set; } = "email";   // email, sms, social
    public string? AudienceFilterJson { get; set; }  // segmentation rules
    public string? TemplateId { get; set; }
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RecipientsCount { get; set; }
    public int SentCount { get; set; }
    public int OpenedCount { get; set; }
    public int ClickedCount { get; set; }
    public int UnsubscribedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CampaignRecipient : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public Guid? ContactId { get; set; }
    public string? Email { get; set; }
    public string Status { get; set; } = "pending"; // pending, sent, failed, unsubscribed
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClickedAt { get; set; }
}

// ---------- Support / Helpdesk ----------

public enum TicketStatus { New, Open, Pending, OnHold, Resolved, Closed }
public enum TicketPriority { Low, Medium, High, Urgent }

public class Ticket : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public string? Category { get; set; }

    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }

    // SLA
    public DateTimeOffset? SlaDueAt { get; set; }
    public bool SlaBreached { get; set; }
    public DateTimeOffset? FirstResponseAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public int SatisfactionScore { get; set; }     // 1-5 CSAT
    public string? Source { get; set; }            // portal, email, chat, api

    public string? CustomFieldsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TicketReply> Replies { get; set; } = new List<TicketReply>();
}

public class TicketReply : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternalNote { get; set; }
    public string? AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Workflow automation ----------

public enum WorkflowTriggerType
{
    Manual, RecordCreated, RecordUpdated, Scheduled, Webhook, ExternalEvent, AiEvent
}

public enum WorkflowRunStatus { Pending, Running, Waiting, Completed, Failed, Cancelled }

public class WorkflowDefinition : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public WorkflowTriggerType TriggerType { get; set; }
    /// <summary>Entity the trigger watches: Contact, Lead, Deal, Ticket, Order, Invoice...</summary>
    public string? TriggerEntity { get; set; }
    /// <summary>JSON trigger config: {event:"created"|"updated"|"stage_changed", field, value, cron}</summary>
    public string TriggerConfigJson { get; set; } = "{}";
    /// <summary>JSON graph: nodes/edges for the visual builder.</summary>
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<WorkflowRun> Runs { get; set; } = new List<WorkflowRun>();
}

public class WorkflowRun : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid WorkflowId { get; set; }
    public WorkflowDefinition? Workflow { get; set; }
    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Pending;
    public string? TriggerPayloadJson { get; set; }
    public string? CurrentStep { get; set; }
    public string? LogJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}

// ---------- Commerce ----------

public enum ProductType { Product, Service }

public class Product : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public ProductType Type { get; set; } = ProductType.Product;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? Cost { get; set; }
    public int StockQuantity { get; set; }
    public bool TrackStock { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ExternalRefId { get; set; }
    public string? CustomFieldsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum OrderStatus { Draft, Confirmed, Processing, Shipped, Delivered, Cancelled, Refunded }
public enum InvoiceStatus { Draft, Sent, Paid, PartiallyPaid, Overdue, Cancelled, Refunded }

public class Order : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? AccountId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    public string? ShippingAddressJson { get; set; }
    public string? Notes { get; set; }
    public string? ExternalRefId { get; set; }
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
}

public class OrderLine : ITenantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class Invoice : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset IssueDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? Notes { get; set; }
    public string? ExternalRefId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}

public class InvoiceLine : ITenantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>Payment provider settings per organization (Stripe etc.). Secrets encrypted at rest.</summary>
public class PaymentProviderConfig : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Provider { get; set; } = "Stripe";
    public bool IsEnabled { get; set; }
    public string? SecretKeyEncrypted { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecretEncrypted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
