namespace Crm.Data.Entities;

public class Pipeline : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<DealStage> Stages { get; set; } = new List<DealStage>();
    public ICollection<Deal> Deals { get; set; } = new List<Deal>();
}

public class DealStage : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }
    public string Name { get; set; } = string.Empty;
    public DealStageType Type { get; set; } = DealStageType.Open;
    public int Probability { get; set; } = 10;   // 0-100 default win probability
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Deal : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }
    public Guid StageId { get; set; }
    public DealStage? Stage { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public int Probability { get; set; }              // effective probability (stage default unless overridden)
    public DateTimeOffset? ExpectedCloseDate { get; set; }
    public DateTimeOffset? ActualCloseDate { get; set; }

    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? LeadId { get; set; }

    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public string? Competitor { get; set; }
    public string? LostReason { get; set; }
    public string? WonReason { get; set; }

    public string? Tags { get; set; }
    public string? CustomFieldsJson { get; set; }
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DealProduct> Products { get; set; } = new List<DealProduct>();
}

public class DealProduct : ITenantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DealId { get; set; }
    public Deal? Deal { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}
