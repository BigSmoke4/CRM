using Crm.Data;
using Crm.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

/// <summary>Populates realistic demo data across all modules for a tenant. Idempotent per org (skips if contacts exist).</summary>
public class DemoDataSeeder
{
    private readonly ApplicationDbContext _db;

    public DemoDataSeeder(ApplicationDbContext db) => _db = db;

    public async Task<int> SeedAsync(Guid orgId, string? userId, CancellationToken ct = default)
    {
        if (await _db.Contacts.AnyAsync(c => c.OrganizationId == orgId, ct)) return 0;

        var rnd = new Random(42);
        string Pick(string[] a) => a[rnd.Next(a.Length)];

        // ---- Accounts ----
        var industries = new[] { "Technology", "Healthcare", "Finance", "Retail", "Manufacturing", "Education" };
        var cities = new[] { "New York", "London", "Berlin", "Singapore", "Toronto", "Sydney" };
        var accounts = new List<Account>();
        for (var i = 1; i <= 8; i++)
        {
            var a = new Account
            {
                OrganizationId = orgId,
                Name = $"{Pick(new[] { "Apex", "Nova", "Vertex", "Pinnacle", "Summit", "Horizon", "Quantum", "Sterling" })} {Pick(new[] { "Corp", "Ltd", "Inc", "Group", "Systems", "Partners" })} {i}",
                Industry = Pick(industries), Website = $"https://example{i}.com",
                City = Pick(cities), Country = "US", Phone = $"+1-555-{1000 + i}",
                AnnualRevenue = rnd.Next(1_000_000, 50_000_000), CompanySize = $"{rnd.Next(10, 5000)} employees"
            };
            accounts.Add(a); _db.Accounts.Add(a);
        }
        await _db.SaveChangesAsync(ct);

        // ---- Contacts ----
        var first = new[] { "James", "Maria", "Ahmed", "Sofia", "Liam", "Priya", "Noah", "Elena", "Marcus", "Yuki", "Omar", "Grace" };
        var last = new[] { "Smith", "Garcia", "Khan", "Rossi", "Brown", "Patel", "Wilson", "Petrova", "Lee", "Tanaka", "Hassan", "Muller" };
        var titles = new[] { "CEO", "CTO", "Sales Director", "Marketing Manager", "CFO", "Operations Lead", "Product Manager", "HR Director" };
        var sources = new[] { "Website", "Referral", "LinkedIn", "Trade Show", "Cold Outreach", "Partner" };
        var contacts = new List<Contact>();
        for (var i = 0; i < 24; i++)
        {
            var fn = first[i % first.Length]; var ln = last[(i * 5 + 3) % last.Length];
            var c = new Contact
            {
                OrganizationId = orgId,
                FirstName = fn, LastName = ln,
                Email = $"{fn}.{ln}{i}@example.com".ToLowerInvariant(),
                Phone = $"+1-555-{2000 + i}", Mobile = $"+1-555-{3000 + i}",
                JobTitle = Pick(titles), AccountId = accounts[i % accounts.Count].Id,
                Source = Pick(sources), CustomerType = i % 4 == 0 ? CustomerType.Customer : i % 4 == 1 ? CustomerType.Prospect : CustomerType.Lead,
                City = Pick(cities), Country = "US", Website = $"https://contact{i}.example.com",
                EmailOptIn = i % 3 != 0, Tags = i % 5 == 0 ? "vip" : null,
                AssignedToId = userId
            };
            contacts.Add(c); _db.Contacts.Add(c);
        }
        await _db.SaveChangesAsync(ct);

        // ---- Leads ----
        var leadStatuses = Enum.GetValues<LeadStatus>();
        for (var i = 0; i < 15; i++)
        {
            _db.Leads.Add(new Lead
            {
                OrganizationId = orgId,
                FirstName = first[(i * 2 + 1) % first.Length], LastName = last[(i * 3 + 1) % last.Length],
                Email = $"lead{i}@prospect{i}.com", Phone = $"+1-555-{4000 + i}",
                CompanyName = $"LeadCo {i + 1}", JobTitle = Pick(titles),
                Source = Pick(sources), Status = leadStatuses[i % leadStatuses.Length],
                Score = rnd.Next(10, 100), AssignedToId = userId
            });
        }

        // ---- Deals across pipeline stages ----
        var pipeline = await _db.Pipelines.Include(p => p.Stages)
            .FirstOrDefaultAsync(p => p.OrganizationId == orgId && p.IsDefault, ct)
            ?? await _db.Pipelines.Include(p => p.Stages).FirstOrDefaultAsync(p => p.OrganizationId == orgId, ct);
        var stages = pipeline?.Stages.OrderBy(s => s.SortOrder).ToList() ?? new List<DealStage>();
        var amounts = new[] { 5000m, 12000m, 25000m, 48000m, 75000m, 120000m, 200000m, 350000m };
        if (pipeline != null && stages.Count > 0)
        for (var i = 0; i < 12; i++)
        {
            var stage = stages[i % stages.Count];
            _db.Deals.Add(new Deal
            {
                OrganizationId = orgId, PipelineId = pipeline.Id,
                Name = $"{Pick(new[] { "Enterprise", "Growth", "Starter", "Annual", "Expansion" })} Deal — {accounts[i % accounts.Count].Name}",
                AccountId = accounts[i % accounts.Count].Id, ContactId = contacts[i].Id,
                StageId = stage.Id, Amount = amounts[i % amounts.Length], Currency = "USD",
                Probability = stage.Probability,
                ExpectedCloseDate = DateTimeOffset.UtcNow.AddDays(rnd.Next(-10, 90)),
                OwnerId = userId
            });
        }

        // ---- Activities ----
        var actTypes = new[] { ActivityType.Call, ActivityType.Meeting, ActivityType.Email, ActivityType.Task, ActivityType.Note };
        for (var i = 0; i < 20; i++)
        {
            _db.Activities.Add(new Activity
            {
                OrganizationId = orgId, Type = actTypes[i % actTypes.Length],
                Subject = $"{actTypes[i % actTypes.Length]} with {contacts[i].FirstName} {contacts[i].LastName}",
                Body = "Demo activity entry.", ContactId = contacts[i].Id, AccountId = contacts[i].AccountId,
                OwnerId = userId, DueAt = DateTimeOffset.UtcNow.AddDays(rnd.Next(-5, 10)),
                IsCompleted = i % 3 == 0, CompletedAt = i % 3 == 0 ? DateTimeOffset.UtcNow.AddDays(-1) : null
            });
        }

        // ---- Tickets ----
        var priorities = Enum.GetValues<TicketPriority>();
        var statuses = Enum.GetValues<TicketStatus>();
        for (var i = 0; i < 6; i++)
        {
            _db.Tickets.Add(new Ticket
            {
                OrganizationId = orgId, TicketNumber = $"T-{DateTime.UtcNow:yyyy}-{(i + 1):D5}",
                Subject = Pick(new[] { "Login issue", "Billing question", "Feature request", "Data sync problem", "How to export", "API error" }),
                Description = "Customer reported an issue via the portal.",
                ContactId = contacts[i].Id, Priority = priorities[i % priorities.Length],
                Status = statuses[i % statuses.Length], Category = "general",
                AssigneeId = userId, Source = "portal",
                SlaDueAt = DateTimeOffset.UtcNow.AddHours(24 * (i + 1)), CreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }

        // ---- Products ----
        var products = new (string Name, string Sku, decimal Price, string Cat)[]
        {
            ("Starter Plan", "PLAN-START", 99m, "Subscription"), ("Pro Plan", "PLAN-PRO", 299m, "Subscription"),
            ("Enterprise Plan", "PLAN-ENT", 999m, "Subscription"), ("Onboarding Package", "SVC-ONB", 1500m, "Service"),
            ("Custom Integration", "SVC-INT", 5000m, "Service"), ("Priority Support", "SVC-SUP", 499m, "Service"),
            ("API Access Add-on", "ADD-API", 199m, "Add-on"), ("Extra Storage 100GB", "ADD-STO", 49m, "Add-on")
        };
        foreach (var (n, sku, price, cat) in products)
            _db.Products.Add(new Product { OrganizationId = orgId, Name = n, Sku = sku, Price = price, Type = cat == "Service" ? ProductType.Service : ProductType.Product, Category = cat, TrackStock = cat != "Subscription", StockQuantity = rnd.Next(0, 100) });

        // ---- Orders + Invoices ----
        var prodList = await _db.Products.Where(p => p.OrganizationId == orgId).ToListAsync(ct);
        for (var i = 0; i < 5; i++)
        {
            var order = new Order
            {
                OrganizationId = orgId, OrderNumber = $"O-{DateTime.UtcNow:yyyy}-{(i + 1):D5}",
                ContactId = contacts[i * 2].Id, Status = i % 3 == 0 ? OrderStatus.Delivered : i % 3 == 1 ? OrderStatus.Shipped : OrderStatus.Processing,
                OrderDate = DateTimeOffset.UtcNow.AddDays(-rnd.Next(0, 20))
            };
            for (var j = 0; j < 2 && prodList.Count > 0; j++)
            {
                var p = prodList[(i + j) % prodList.Count];
                order.Lines.Add(new OrderLine { Description = p.Name, Quantity = rnd.Next(1, 4), UnitPrice = p.Price, LineTotal = p.Price * rnd.Next(1, 4) });
            }
            order.Subtotal = order.Lines.Sum(l => l.LineTotal); order.Total = order.Subtotal;
            _db.Orders.Add(order);

            var inv = new Invoice
            {
                OrganizationId = orgId, InvoiceNumber = $"INV-{DateTime.UtcNow:yyyy}-{(i + 1):D5}",
                ContactId = contacts[i * 2].Id, OrderId = order.Id,
                Status = i == 0 ? InvoiceStatus.Paid : i == 1 ? InvoiceStatus.Sent : i == 2 ? InvoiceStatus.Overdue : InvoiceStatus.Draft,
                DueDate = DateTimeOffset.UtcNow.AddDays(30 - i * 5),
                AmountPaid = i == 0 ? order.Total : 0
            };
            foreach (var l in order.Lines)
                inv.Lines.Add(new InvoiceLine { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, LineTotal = l.LineTotal });
            inv.Subtotal = inv.Lines.Sum(l => l.LineTotal); inv.Total = inv.Subtotal;
            _db.Invoices.Add(inv);
        }

        // ---- Campaign ----
        _db.Campaigns.Add(new Campaign
        {
            OrganizationId = orgId, Name = "Q1 Product Newsletter", Channel = "email",
            Subject = "What's new this quarter", Status = CampaignStatus.Completed,
            BodyHtml = "<h2>Hi {{name}}!</h2><p>Here's what's new…</p>",
            RecipientsCount = contacts.Count(c => c.EmailOptIn), SentCount = contacts.Count(c => c.EmailOptIn),
            StartedAt = DateTimeOffset.UtcNow.AddDays(-7), FinishedAt = DateTimeOffset.UtcNow.AddDays(-7).AddHours(1)
        });

        // ---- Workflow ----
        _db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            OrganizationId = orgId, Name = "Hot lead follow-up", IsActive = true,
            TriggerType = WorkflowTriggerType.RecordCreated, TriggerEntity = "Lead",
            DefinitionJson = """{"steps":[{"type":"condition","field":"score","op":">","value":70},{"type":"action","action":"create_task","subject":"Call hot lead within 24h","dueDays":1},{"type":"action","action":"send_notification","title":"Hot lead assigned"}]}"""
        });

        // ---- Knowledge doc ----
        _db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            OrganizationId = orgId, Title = "Company FAQ", SourceType = "text", Status = "indexed",
            ExtractedText = "Q: What are your business hours? A: Monday-Friday 9am-6pm EST.\nQ: Do you offer refunds? A: Yes, 30-day money-back guarantee.\nQ: How do I export data? A: Go to Settings > Reports and click Export CSV."
        });

        // ---- Notifications ----
        _db.Notifications.AddRange(
            new Notification { OrganizationId = orgId, UserId = userId, Title = "Welcome to your CRM", Body = "Demo data has been loaded. Explore the modules!", Type = "info" },
            new Notification { OrganizationId = orgId, UserId = userId, Title = "3 tickets need attention", Body = "Open tickets with priority High or above.", Type = "warning" });

        await _db.SaveChangesAsync(ct);
        return 1;
    }
}
