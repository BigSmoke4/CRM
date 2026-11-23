using System.Text;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>AI knowledge base: ingest documents, chunk, index for RAG retrieval.</summary>
[Authorize]
public class KnowledgeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly EmbeddingService _embeddings;
    private readonly ISecretProtector _secrets;

    public KnowledgeController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit, EmbeddingService embeddings, ISecretProtector secrets)
    { _db = db; _tenant = tenant; _audit = audit; _embeddings = embeddings; _secrets = secrets; }

    [HttpGet, Authorize(Policy = Permissions.AiView)]
    public async Task<IActionResult> Index()
        => View(await _db.KnowledgeDocuments.OrderByDescending(d => d.CreatedAt).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> Upload(IFormFile file, string? title)
    {
        if (file == null || file.Length == 0) { TempData["Error"] = "No file."; return RedirectToAction(nameof(Index)); }
        if (file.Length > 20 * 1024 * 1024) { TempData["Error"] = "Max 20 MB."; return RedirectToAction(nameof(Index)); }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".txt", ".md", ".csv", ".json", ".html", ".log" };
        if (!allowed.Contains(ext)) { TempData["Error"] = "Supported: txt, md, csv, json, html."; return RedirectToAction(nameof(Index)); }

        string text;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true))
            text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text)) { TempData["Error"] = "File is empty."; return RedirectToAction(nameof(Index)); }

        var doc = new KnowledgeDocument
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title,
            SourceType = "upload", ContentType = file.ContentType,
            SizeBytes = file.Length, Status = "pending"
        };
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        await IndexDocumentAsync(doc, text);
        await _audit.LogAsync("Ai.KnowledgeUpload", "KnowledgeDocument", doc.Id.ToString(), new { doc.Title });
        TempData["Success"] = $"'{doc.Title}' indexed ({doc.Status}).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Chunk text (~900 chars, 150 overlap) and store chunks for retrieval.</summary>
    private async Task IndexDocumentAsync(KnowledgeDocument doc, string text, CancellationToken ct = default)
    {
        const int size = 900, overlap = 150;
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += size - overlap)
        {
            var len = Math.Min(size, text.Length - i);
            chunks.Add(text.Substring(i, len));
            if (i + len >= text.Length) break;
        }

        // clear old chunks
        var old = await _db.KnowledgeChunks.Where(c => c.DocumentId == doc.Id).ToListAsync();
        _db.KnowledgeChunks.RemoveRange(old);

        var idx = 0;
        var embeddingTasks = new List<(KnowledgeChunk Entity, string Text)>();
        foreach (var chunk in chunks)
        {
            var chunkEntity = new KnowledgeChunk
            {
                OrganizationId = doc.OrganizationId, DocumentId = doc.Id,
                ChunkIndex = idx++, Content = chunk, TokenCount = chunk.Length / 4
            };
            _db.KnowledgeChunks.Add(chunkEntity);
            embeddingTasks.Add((chunkEntity, chunk));
        }
        await _db.SaveChangesAsync(ct);
        await ApplyEmbeddingsAsync(embeddingTasks, ct);
        doc.ExtractedText = text.Length > 50000 ? text[..50000] : text;
        doc.Status = "indexed"; doc.IndexedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var doc = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound();
        var chunks = await _db.KnowledgeChunks.Where(c => c.DocumentId == id).ToListAsync();
        _db.KnowledgeChunks.RemoveRange(chunks);
        _db.KnowledgeDocuments.Remove(doc);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Quick-add text note as a knowledge doc.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> AddText(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) { TempData["Error"] = "Content required."; return RedirectToAction(nameof(Index)); }
        var doc = new KnowledgeDocument
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            Title = string.IsNullOrWhiteSpace(title) ? "Note " + DateTime.UtcNow.ToString("yyyy-MM-dd") : title,
            SourceType = "text", Status = "pending"
        };
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        await IndexDocumentAsync(doc, content);
        TempData["Success"] = "Note indexed.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ApplyEmbeddingsAsync(List<(KnowledgeChunk Entity, string Text)> tasks, CancellationToken ct)
    {
        // Use default enabled provider that supports embeddings
        var provider = await _db.AiProviderConfigs
            .Where(p => p.IsEnabled && (p.ProviderType == AiProviderType.OpenAI || p.ProviderType == AiProviderType.OpenAICompatible || p.ProviderType == AiProviderType.Gemini))
            .OrderByDescending(p => p.IsDefault).FirstOrDefaultAsync(ct);
        if (provider == null) return;
        var key = _secrets.UnprotectOrNull(provider.ApiKeyEncrypted);
        if (string.IsNullOrEmpty(key)) return;
        foreach (var (entity, text) in tasks)
        {
            var vec = await _embeddings.EmbedAsync(provider, key, text[..Math.Min(2000, text.Length)], ct);
            if (vec != null) entity.EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(vec);
        }
        await _db.SaveChangesAsync(ct);
    }
}
