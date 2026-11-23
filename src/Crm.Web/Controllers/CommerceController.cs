using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Products, orders and invoices — commerce module.</summary>
[Authorize]
public class CommerceController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public CommerceController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    // ---------- Products ----------
    [HttpGet, Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> Products(string? q)
    {
        var query = _db.Products.Where(p => p.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(t) || (p.Sku != null && p.Sku.ToLower().Contains(t)));
        }
        ViewBag.Q = q;
        return View(await query.OrderBy(p => p.Name).Take(500).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.ProductsCreate)]
    public async Task<IActionResult> ProductCreate(Product p)
    {
        p.Id = Guid.NewGuid(); p.OrganizationId = _tenant.OrganizationId!.Value;
        p.CreatedAt = DateTimeOffset.UtcNow; p.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Products.Add(p); await _db.SaveChangesAsync();
        TempData["Success"] = "Product created.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.ProductsEdit)]
    public async Task<IActionResult> ProductUpdate(Guid id, Product p)
    {
        var e = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.Name = p.Name; e.Sku = p.Sku; e.Description = p.Description; e.Type = p.Type;
        e.Price = p.Price; e.Cost = p.Cost; e.StockQuantity = p.StockQuantity;
        e.TrackStock = p.TrackStock; e.Category = p.Category; e.IsActive = p.IsActive;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Products));
    }

    // ---------- Orders ----------
    [HttpGet, Authorize(Policy = Permissions.OrdersView)]
    public async Task<IActionResult> Orders(string? status)
    {
        var query = _db.Orders.Include(o => o.Contact).Include(o => o.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, out var s))
            query = query.Where(o => o.Status == s);
        ViewBag.Status = status;
        return View(await query.OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.OrdersCreate)]
    public async Task<IActionResult> OrderCreate(Guid? contactId, string? notes)
    {
        var order = new Order
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            OrderNumber = $"O-{DateTime.UtcNow:yyyy}-{await _db.Orders.CountAsync() + 1:D5}",
            ContactId = contactId, Notes = notes, Status = OrderStatus.Draft
        };
        _db.Orders.Add(order); await _db.SaveChangesAsync();
        TempData["Success"] = $"Order {order.OrderNumber} created.";
        return RedirectToAction(nameof(OrderDetails), new { id = order.Id });
    }

    [HttpGet, Authorize(Policy = Permissions.OrdersView)]
    public async Task<IActionResult> OrderDetails(Guid id)
    {
        var o = await _db.Orders.Include(x => x.Contact).Include(x => x.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        ViewBag.Products = await _db.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
        return View(o);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.OrdersEdit)]
    public async Task<IActionResult> AddOrderLine(Guid orderId, Guid productId, int qty)
    {
        var o = await _db.Orders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId);
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (o == null || p == null || qty <= 0) return NotFound();
        o.Lines.Add(new OrderLine
        {
            OrganizationId = o.OrganizationId, ProductId = p.Id, Description = p.Name,
            Quantity = qty, UnitPrice = p.Price, LineTotal = p.Price * qty
        });
        RecalcOrder(o);
        if (p.TrackStock) p.StockQuantity = Math.Max(0, p.StockQuantity - qty);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(OrderDetails), new { id = orderId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.OrdersEdit)]
    public async Task<IActionResult> SetOrderStatus(Guid id, OrderStatus status)
    {
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        o.Status = status;
        if (status == OrderStatus.Delivered || status == OrderStatus.Shipped) o.PaidAt ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(OrderDetails), new { id });
    }

    // ---------- Invoices ----------
    [HttpGet, Authorize(Policy = Permissions.InvoicesView)]
    public async Task<IActionResult> Invoices(string? status)
    {
        var query = _db.Invoices.Include(i => i.Contact).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, out var s))
            query = query.Where(i => i.Status == s);
        ViewBag.Status = status;
        return View(await query.OrderByDescending(i => i.CreatedAt).Take(300).ToListAsync());
    }

    [HttpGet, Authorize(Policy = Permissions.InvoicesCreate)]
    public async Task<IActionResult> InvoiceCreate(Guid? orderId, Guid? contactId)
    {
        ViewBag.Contacts = await _db.Contacts.Where(c => !c.IsDeleted).OrderBy(c => c.LastName).Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName }).ToListAsync();
        ViewBag.Orders = await _db.Orders.Where(o => o.Status != OrderStatus.Cancelled).Select(o => new { o.Id, o.OrderNumber }).ToListAsync();
        return View(new InvoiceFormVm { OrderId = orderId, ContactId = contactId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.InvoicesCreate)]
    public async Task<IActionResult> InvoiceCreate(InvoiceFormVm vm)
    {
        var inv = new Invoice
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            InvoiceNumber = $"INV-{DateTime.UtcNow:yyyy}-{await _db.Invoices.CountAsync() + 1:D5}",
            ContactId = vm.ContactId, OrderId = vm.OrderId,
            DueDate = vm.DueDate ?? DateTimeOffset.UtcNow.AddDays(30),
            Notes = vm.Notes, Status = InvoiceStatus.Draft
        };
        // pull lines from order if linked
        if (vm.OrderId.HasValue)
        {
            var order = await _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == vm.OrderId);
            if (order != null)
                foreach (var l in order.Lines)
                    inv.Lines.Add(new InvoiceLine { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, LineTotal = l.LineTotal });
        }
        RecalcInvoice(inv);
        _db.Invoices.Add(inv); await _db.SaveChangesAsync();
        TempData["Success"] = $"Invoice {inv.InvoiceNumber} created.";
        return RedirectToAction(nameof(Invoices));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.InvoicesEdit)]
    public async Task<IActionResult> SetInvoiceStatus(Guid id, InvoiceStatus status)
    {
        var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.Id == id);
        if (inv == null) return NotFound();
        inv.Status = status;
        if (status == InvoiceStatus.Paid) { inv.AmountPaid = inv.Total; inv.PaidAt = DateTimeOffset.UtcNow; }
        if (status == InvoiceStatus.Sent && inv.Status == InvoiceStatus.Draft) { }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Invoices));
    }

    private static void RecalcOrder(Order o)
    {
        o.Subtotal = o.Lines.Sum(l => l.LineTotal);
        o.Total = o.Subtotal - o.Discount + o.Tax;
        o.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static void RecalcInvoice(Invoice i)
    {
        i.Subtotal = i.Lines.Sum(l => l.LineTotal);
        i.Total = i.Subtotal + i.Tax;
        i.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
