using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>AI provider configuration + assistant chat with tool use and RAG.</summary>
[Authorize]
public class AiController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IAuditService _audit;
    private readonly EmbeddingService _embeddings;

    public AiController(ApplicationDbContext db, ITenantContext tenant, ISecretProtector secrets,
        IHttpClientFactory httpFactory, IAuditService audit, EmbeddingService embeddings)
    { _db = db; _tenant = tenant; _secrets = secrets; _httpFactory = httpFactory; _audit = audit; _embeddings = embeddings; }

    // ---------- Providers ----------
    [HttpGet, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> Providers()
        => View(await _db.AiProviderConfigs.OrderByDescending(p => p.IsDefault).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> ProviderSave(AiProviderConfigVM vm)
    {
        AiProviderConfig? p = vm.Id.HasValue
            ? await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.Id == vm.Id)
            : new AiProviderConfig { OrganizationId = _tenant.OrganizationId!.Value };
        if (p == null) return NotFound();

        p.Name = vm.Name; p.ProviderType = vm.ProviderType; p.Endpoint = vm.Endpoint;
        p.Model = vm.Model; p.Temperature = vm.Temperature; p.MaxTokens = vm.MaxTokens;
        p.IsEnabled = vm.IsEnabled; p.MonthlyTokenBudget = vm.MonthlyTokenBudget;
        if (!string.IsNullOrEmpty(vm.ApiKey) && vm.ApiKey != "••••••••")
            p.ApiKeyEncrypted = _secrets.Protect(vm.ApiKey);
        if (vm.IsDefault) await ClearDefaultAsync();
        p.IsDefault = vm.IsDefault;

        if (vm.Id == null) _db.AiProviderConfigs.Add(p);
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "AI provider saved.";
        return RedirectToAction(nameof(Providers));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> ProviderDelete(Guid id)
    {
        var p = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (p != null) { _db.AiProviderConfigs.Remove(p); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Providers));
    }

    /// <summary>AJAX: test provider connectivity with a minimal chat completion.</summary>
    [HttpPost, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> TestProvider(Guid id, CancellationToken ct)
    {
        var p = await _db.AiProviderConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        var result = await ChatCompletionAsync(p, "Reply with the single word: ok", null, ct);
        return Json(new { ok = result.Ok, result.Error, result.Preview });
    }

    // ---------- Assistants ----------
    [HttpGet, Authorize(Policy = Permissions.AiUse)]
    public async Task<IActionResult> Index()
    {
        ViewBag.Assistants = await _db.AiAssistants.Include(a => a.Provider)
            .Where(a => a.IsEnabled).ToListAsync();
        ViewBag.Conversations = await _db.AiConversations
            .Where(c => c.UserId == _tenant.UserId).OrderByDescending(c => c.UpdatedAt).Take(30).ToListAsync();
        ViewBag.Providers = await _db.AiProviderConfigs.Where(p => p.IsEnabled).ToListAsync();
        return View();
    }

    [HttpGet, Authorize(Policy = Permissions.AiUse)]
    public async Task<IActionResult> Chat(Guid? assistantId, Guid? conversationId)
    {
        var assistants = await _db.AiAssistants.Include(a => a.Provider).Where(a => a.IsEnabled).ToListAsync();
        if (!assistants.Any())
        {
            TempData["Error"] = "No AI assistant configured. Ask an admin to set up AI Providers.";
            return RedirectToAction(nameof(Index));
        }
        var assistant = assistants.FirstOrDefault(a => a.Id == assistantId) ?? assistants.First();

        AiConversation conv;
        if (conversationId.HasValue)
        {
            conv = await _db.AiConversations.Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == _tenant.UserId)
                ?? new AiConversation { OrganizationId = _tenant.OrganizationId!.Value, AssistantId = assistant.Id, UserId = _tenant.UserId, Title = "New chat" };
        }
        else
        {
            conv = new AiConversation { OrganizationId = _tenant.OrganizationId!.Value, AssistantId = assistant.Id, UserId = _tenant.UserId, Title = "New chat" };
            _db.AiConversations.Add(conv); await _db.SaveChangesAsync();
        }
        if (conv.Id == Guid.Empty) { _db.AiConversations.Add(conv); await _db.SaveChangesAsync(); }

        ViewBag.Assistant = assistant;
        ViewBag.Assistants = assistants;
        ViewBag.Conversations = await _db.AiConversations.Where(c => c.UserId == _tenant.UserId)
            .OrderByDescending(c => c.UpdatedAt).Take(30).ToListAsync();
        ViewBag.Messages = await _db.AiMessages.Where(m => m.ConversationId == conv.Id)
            .OrderBy(m => m.CreatedAt).ToListAsync();
        return View(conv);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiUse)]
    public async Task<IActionResult> ChatSend(Guid conversationId, string message)
    {
        var conv = await _db.AiConversations.Include(c => c.Assistant).ThenInclude(a => a!.Provider)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == _tenant.UserId);
        if (conv?.Assistant?.Provider == null) return NotFound();
        if (string.IsNullOrWhiteSpace(message)) return RedirectToAction(nameof(Chat), new { conversationId });

        _db.AiMessages.Add(new AiMessage
        {
            OrganizationId = conv.OrganizationId, ConversationId = conv.Id,
            Role = "user", Content = message
        });

        // Build context: recent history + RAG retrieval + CRM tool data
        var history = await _db.AiMessages.Where(m => m.ConversationId == conv.Id)
            .OrderByDescending(m => m.CreatedAt).Take(20).ToListAsync();
        history.Reverse();

        var rag = await RetrieveKnowledgeAsync(conv.OrganizationId, message, 4);
        var systemPrompt = BuildSystemPrompt(conv.Assistant, rag);

        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var m in history)
            messages.Add(new { role = m.Role == "tool" ? "user" : m.Role, content = m.Content });

        var result = await ChatCompletionAsync(conv.Assistant.Provider, messages, _tenant.OrganizationId, ct: default);
        if (!result.Ok)
        {
            _db.AiMessages.Add(new AiMessage
            {
                OrganizationId = conv.OrganizationId, ConversationId = conv.Id,
                Role = "assistant", Content = $"⚠ AI request failed: {result.Error}"
            });
        }
        else
        {
            _db.AiMessages.Add(new AiMessage
            {
                OrganizationId = conv.OrganizationId, ConversationId = conv.Id,
                Role = "assistant", Content = result.Text ?? "",
                MetadataJson = result.CitationsJson
            });
        }
        conv.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Ai.Chat", "AiConversation", conv.Id.ToString());
        return RedirectToAction(nameof(Chat), new { conversationId });
    }

    // ---------- RAG ----------
    private async Task<List<(string Title, string Snippet)>> RetrieveKnowledgeAsync(Guid orgId, string query, int topK)
    {
        // Simple keyword retrieval over indexed documents (embeddings upgrade path noted in docs)
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2).Take(8).ToList();
        if (!terms.Any()) return new();

        var docs = await _db.KnowledgeDocuments
            .Where(d => d.Status == "indexed" && d.ExtractedText != null)
            .Select(d => new { d.Title, d.ExtractedText }).Take(500).ToListAsync();

        // Try vector retrieval first: embed the query, cosine-similarity over chunk embeddings
        var provider = await _db.AiProviderConfigs
            .Where(p => p.IsEnabled && (p.ProviderType == AiProviderType.OpenAI || p.ProviderType == AiProviderType.OpenAICompatible || p.ProviderType == AiProviderType.Gemini))
            .OrderByDescending(p => p.IsDefault).FirstOrDefaultAsync();
        if (provider != null)
        {
            var key = _secrets.UnprotectOrNull(provider.ApiKeyEncrypted);
            if (!string.IsNullOrEmpty(key))
            {
                var qVec = await _embeddings.EmbedAsync(provider, key, query, default);
                if (qVec != null)
                {
                    var chunks = await _db.KnowledgeChunks
                        .Where(c => c.EmbeddingJson != null && c.Document.Status == "indexed")
                        .Select(c => new { c.Content, c.EmbeddingJson, c.Document.Title })
                        .Take(2000).ToListAsync();
                    var scoredChunks = chunks
                        .Select(c => (c.Title, c.Content, Score: EmbeddingService.Cosine(qVec, EmbeddingService.ParseVector(c.EmbeddingJson) ?? Array.Empty<float>())))
                        .Where(x => x.Score > 0.15)
                        .OrderByDescending(x => x.Score).Take(topK).ToList();
                    if (scoredChunks.Any())
                        return scoredChunks.Select(s => (s.Title, s.Content.Length > 1200 ? s.Content[..1200] : s.Content)).ToList();
                }
            }
        }

        // Fallback: keyword retrieval
        var scored = docs.Select(d =>
        {
            var text = (d.Title + " " + d.ExtractedText!).ToLowerInvariant();
            var score = terms.Count(t => text.Contains(t));
            return (d.Title, d.ExtractedText!, score);
        }).Where(x => x.score > 0).OrderByDescending(x => x.score).Take(topK).ToList();

        return scored.Select(s => (s.Title, s.Item2.Length > 1200 ? s.Item2[..1200] : s.Item2)).ToList();
    }

    private static string BuildSystemPrompt(AiAssistant a, List<(string Title, string Snippet)> rag)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are {a.Name}, an AI assistant for a CRM system.");
        if (!string.IsNullOrWhiteSpace(a.Instructions)) sb.AppendLine(a.Instructions);
        sb.AppendLine("Answer concisely. When using company knowledge, cite the document title.");
        sb.AppendLine("You may help draft emails, summarize records, and suggest next actions. Never fabricate data.");
        if (rag.Any())
        {
            sb.AppendLine("\n=== COMPANY KNOWLEDGE ===");
            foreach (var (title, snippet) in rag)
                sb.AppendLine($"[{title}] {snippet}\n---");
        }
        return sb.ToString();
    }

    // ---------- Provider call ----------
    private async Task<(bool Ok, string? Text, string? Error, string? CitationsJson, string? Preview)> ChatCompletionAsync(
        AiProviderConfig p, string userMessage, Guid? orgId, CancellationToken ct)
        => await ChatCompletionAsync(p, new List<object> { new { role = "user", content = userMessage } }, orgId, ct);

    private async Task<(bool Ok, string? Text, string? Error, string? CitationsJson, string? Preview)> ChatCompletionAsync(
        AiProviderConfig p, List<object> messages, Guid? orgId, CancellationToken ct)
    {
        var apiKey = _secrets.UnprotectOrNull(p.ApiKeyEncrypted);
        if (string.IsNullOrEmpty(apiKey)) return (false, null, "No API key configured", null, null);

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);

        // OpenAI-compatible chat completions (works for OpenAI, Azure-compat, local vLLM/Ollama, Gemini OpenAI-compat endpoint)
        var endpoint = string.IsNullOrWhiteSpace(p.Endpoint)
            ? p.ProviderType switch
            {
                AiProviderType.Anthropic => "https://api.anthropic.com/v1/messages",
                AiProviderType.Gemini => $"https://generativelanguage.googleapis.com/v1beta/models/{p.Model}:generateContent",
                _ => "https://api.openai.com/v1/chat/completions"
            }
            : p.Endpoint;

        object payload; HttpRequestMessage request;
        if (p.ProviderType == AiProviderType.Anthropic)
        {
            payload = new
            {
                model = p.Model, max_tokens = p.MaxTokens, temperature = p.Temperature,
                messages = messages.Select(m => new { role = ((dynamic)m).role, content = ((dynamic)m).content }).ToList()
            };
            request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (p.ProviderType == AiProviderType.Gemini)
        {
            var contents = messages.Where(m => ((dynamic)m).role != "system")
                .Select(m => new { role = ((dynamic)m).role == "assistant" ? "model" : "user", parts = new[] { new { text = ((dynamic)m).content } } }).ToList();
            var sys = messages.FirstOrDefault(m => ((dynamic)m).role == "system");
            payload = new
            {
                contents,
                systemInstruction = sys != null ? new { parts = new[] { new { text = ((dynamic)sys).content } } } : null,
                generationConfig = new { temperature = p.Temperature, maxOutputTokens = p.MaxTokens }
            };
            request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}?key={apiKey}")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        }
        else
        {
            payload = new { model = p.Model, temperature = p.Temperature, max_tokens = p.MaxTokens, messages };
            request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var resp = await client.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}", null, null);

            using var doc = JsonDocument.Parse(body);
            string? text = p.ProviderType switch
            {
                AiProviderType.Anthropic => doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString(),
                AiProviderType.Gemini => doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString(),
                _ => doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            };

            // track usage
            if (orgId.HasValue)
            {
                var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u : default;
                int tokens = 0;
                if (usage.ValueKind == JsonValueKind.Object)
                    tokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32()
                        : usage.TryGetProperty("totalTokenCount", out var tc) ? tc.GetInt32() : 0;
                if (tokens == 0) tokens = (text?.Length ?? 0) / 4;
                p.TokensUsedThisMonth += tokens;
            }
            return (true, text, null, null, body[..Math.Min(500, body.Length)]);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message, null, null);
        }
    }

    private async Task ClearDefaultAsync()
    {
        var others = await _db.AiProviderConfigs.Where(p => p.IsDefault).ToListAsync();
        foreach (var o in others) o.IsDefault = false;
    }

    public class AiProviderConfigVM
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = "";
        public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAI;
        public string? ApiKey { get; set; }
        public string? Endpoint { get; set; }
        public string Model { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 2000;
        public int MonthlyTokenBudget { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsDefault { get; set; }
    }

    // ---------- Assistants CRUD ----------
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> AssistantSave(AiAssistant a)
    {
        if (a.Id == Guid.Empty)
        {
            a.Id = Guid.NewGuid(); a.OrganizationId = _tenant.OrganizationId!.Value;
            _db.AiAssistants.Add(a);
        }
        else
        {
            var e = await _db.AiAssistants.FirstOrDefaultAsync(x => x.Id == a.Id);
            if (e == null) return NotFound();
            e.Name = a.Name; e.Description = a.Description; e.Instructions = a.Instructions;
            e.ProviderId = a.ProviderId; e.ModelOverride = a.ModelOverride;
            e.AllowedToolsJson = a.AllowedToolsJson; e.KnowledgeScopeJson = a.KnowledgeScopeJson;
            e.RequiresApprovalJson = a.RequiresApprovalJson; e.IsEnabled = a.IsEnabled;
            e.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Assistant saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.AiManage)]
    public async Task<IActionResult> AssistantDelete(Guid id)
    {
        var a = await _db.AiAssistants.FirstOrDefaultAsync(x => x.Id == id);
        if (a != null) { _db.AiAssistants.Remove(a); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
