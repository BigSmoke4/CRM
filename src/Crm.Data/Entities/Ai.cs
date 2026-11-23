namespace Crm.Data.Entities;

public enum AiProviderType
{
    OpenAI, Anthropic, Gemini, OpenAICompatible, Local
}

/// <summary>A configured AI provider (API credentials encrypted at rest).</summary>
public class AiProviderConfig : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public AiProviderType ProviderType { get; set; }
    public string? ApiKeyEncrypted { get; set; }
    public string? Endpoint { get; set; }          // optional override for compatible providers
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int MonthlyTokenBudget { get; set; }    // 0 = unlimited
    public int TokensUsedThisMonth { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A knowledge-base document ingested for RAG.</summary>
public class KnowledgeDocument : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Title { get; set; } = string.Empty;
    /// <summary>upload, url, crm-entity, integration</summary>
    public string SourceType { get; set; } = "upload";
    public string? SourceRef { get; set; }          // entity id / url / integration id
    public string? StoredPath { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? ExtractedText { get; set; }      // capped; full text chunked separately
    public string? MetadataJson { get; set; }

    public string Status { get; set; } = "pending"; // pending, indexed, failed
    public string? Error { get; set; }
    public DateTimeOffset? IndexedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A chunk of a document with its embedding vector (JSON float array).</summary>
public class KnowledgeChunk : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid DocumentId { get; set; }
    public KnowledgeDocument? Document { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    /// <summary>Embedding vector stored as JSON array of floats.</summary>
    public string? EmbeddingJson { get; set; }
    public int? TokenCount { get; set; }
}

/// <summary>A configurable AI assistant with scoped tools/permissions.</summary>
public class AiAssistant : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public Guid? ProviderId { get; set; }
    public AiProviderConfig? Provider { get; set; }
    public string? ModelOverride { get; set; }

    /// <summary>JSON array of allowed tool names (see AiToolCatalog).</summary>
    public string AllowedToolsJson { get; set; } = "[]";
    /// <summary>JSON array of knowledge document ids / "*" for all org docs.</summary>
    public string? KnowledgeScopeJson { get; set; }
    /// <summary>Actions requiring human confirmation: JSON array of tool names.</summary>
    public string RequiresApprovalJson { get; set; } = "[]";
    public int MaxToolCallsPerMessage { get; set; } = 5;
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AiConversation : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? AssistantId { get; set; }
    public AiAssistant? Assistant { get; set; }
    public string? UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}

public class AiMessage : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public string Role { get; set; } = string.Empty;  // user, assistant, tool, system
    public string Content { get; set; } = string.Empty;
    /// <summary>JSON: tool calls / results / citations.</summary>
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
