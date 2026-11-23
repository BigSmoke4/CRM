using System.Text;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Web.Controllers;

/// <summary>CSV/JSON import wizard: upload → map → preview → commit.</summary>
[Authorize]
public class ImportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public ImportController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    // Canonical contact headers → entity fields
    private static readonly Dictionary<string, string> ContactColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firstname"] = "FirstName", ["first_name"] = "FirstName", ["fname"] = "FirstName",
        ["lastname"] = "LastName", ["last_name"] = "LastName", ["lname"] = "LastName",
        ["email"] = "Email", ["emailaddress"] = "Email", ["e-mail"] = "Email",
        ["phone"] = "Phone", ["mobile"] = "Mobile", ["phonenumber"] = "Phone",
        ["jobtitle"] = "JobTitle", ["title"] = "JobTitle", ["position"] = "JobTitle",
        ["accountname"] = "AccountName", ["company"] = "AccountName", ["account"] = "AccountName", ["organization"] = "AccountName",
        ["addressline1"] = "AddressLine1", ["address"] = "AddressLine1", ["street"] = "AddressLine1",
        ["city"] = "City", ["state"] = "State", ["region"] = "State",
        ["postalcode"] = "PostalCode", ["zip"] = "PostalCode", ["postcode"] = "PostalCode",
        ["country"] = "Country", ["website"] = "Website", ["url"] = "Website",
        ["linkedin"] = "LinkedInUrl", ["linkedinurl"] = "LinkedInUrl",
        ["facebook"] = "FacebookUrl", ["facebookurl"] = "FacebookUrl",
        ["whatsapp"] = "WhatsAppNumber", ["whatsappnumber"] = "WhatsAppNumber",
        ["source"] = "Source", ["leadsource"] = "Source",
        ["tags"] = "Tags", ["tag"] = "Tags",
        ["notes"] = "Notes", ["note"] = "Notes",
        ["customertype"] = "CustomerType", ["customertype"] = "CustomerType",
    };

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContactsImport)]
    public async Task<IActionResult> Contacts(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please choose a CSV file.";
            return RedirectToAction("Import", "Contacts");
        }
        if (file.Length > 10 * 1024 * 1024)
        {
            TempData["Error"] = "File too large (max 10 MB).";
            return RedirectToAction("Import", "Contacts");
        }
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Only CSV files are supported in this step.";
            return RedirectToAction("Import", "Contacts");
        }

        List<string[]> rows;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var all = new List<string[]>();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null && all.Count < 5001)
                all.Add(ParseCsvLine(line));
            rows = all;
        }

        if (rows.Count < 2)
        {
            TempData["Error"] = "CSV must have a header row and at least one data row.";
            return RedirectToAction("Import", "Contacts");
        }

        var headers = rows[0].Select(h => h.Trim()).ToArray();
        var mapping = headers.Select(h => ContactColumns.TryGetValue(h, out var f) ? f : null).ToArray();
        var unmapped = headers.Where((h, i) => mapping[i] == null && !string.IsNullOrEmpty(h)).ToList();

        var dataRows = rows.Skip(1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).Take(5000).ToList();
        var preview = dataRows.Take(50).Select(r =>
        {
            var dict = new Dictionary<string, string?>();
            for (var i = 0; i < headers.Length && i < r.Length; i++)
                dict[headers[i]] = r[i];
            return dict;
        }).ToList();

        // Stash parsed rows in TempData? Too big. Instead re-upload approach: store in session cache via IMemoryCache.
        var batchId = Guid.NewGuid().ToString("N");
        var cache = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        cache.Set($"import:{batchId}", new ImportBatch { Headers = headers, Mapping = mapping, Rows = dataRows }, TimeSpan.FromMinutes(15));

        ViewBag.BatchId = batchId;
        ViewBag.FileName = file.FileName;
        ViewBag.RowCount = dataRows.Count;
        ViewBag.Unmapped = unmapped;
        ViewBag.Preview = preview;
        return View("Preview", null);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContactsImport)]
    public async Task<IActionResult> ContactsCommit(string batchId, bool skipDuplicates = true)
    {
        var cache = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var batch = cache.Get<ImportBatch>($"import:{batchId}");
        if (batch == null)
        {
            TempData["Error"] = "Import session expired. Please upload again.";
            return RedirectToAction("Import", "Contacts");
        }

        var orgId = _tenant.OrganizationId!.Value;
        var existingEmails = await _db.Contacts.Where(c => !c.IsDeleted && c.Email != null)
            .Select(c => c.Email!).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

        // Preload accounts by name for linking
        var accountNames = batch.Rows.Select(r => Cell(r, batch.Headers, batch.Mapping, "AccountName"))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var accounts = await _db.Accounts.Where(a => !a.IsDeleted && accountNames.Contains(a.Name))
            .ToDictionaryAsync(a => a.Name, StringComparer.OrdinalIgnoreCase);

        var created = 0; var skipped = 0; var errors = new List<string>();
        foreach (var row in batch.Rows)
        {
            try
            {
                var email = Cell(row, batch.Headers, batch.Mapping, "Email");
                if (skipDuplicates && !string.IsNullOrEmpty(email) && existingEmails.Contains(email))
                { skipped++; continue; }

                var first = Cell(row, batch.Headers, batch.Mapping, "FirstName");
                var last = Cell(row, batch.Headers, batch.Mapping, "LastName");
                if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
                { skipped++; continue; }

                var accName = Cell(row, batch.Headers, batch.Mapping, "AccountName");
                Guid? accountId = null;
                if (!string.IsNullOrWhiteSpace(accName))
                {
                    if (!accounts.TryGetValue(accName, out var acc))
                    {
                        acc = new Account { OrganizationId = orgId, Name = accName };
                        _db.Accounts.Add(acc);
                        await _db.SaveChangesAsync();
                        accounts[accName] = acc;
                    }
                    accountId = acc.Id;
                }

                var contact = new Contact
                {
                    OrganizationId = orgId,
                    FirstName = first ?? "", LastName = last ?? "",
                    Email = email, Phone = Cell(row, batch.Headers, batch.Mapping, "Phone"),
                    Mobile = Cell(row, batch.Headers, batch.Mapping, "Mobile"),
                    JobTitle = Cell(row, batch.Headers, batch.Mapping, "JobTitle"),
                    AccountId = accountId,
                    AddressLine1 = Cell(row, batch.Headers, batch.Mapping, "AddressLine1"),
                    City = Cell(row, batch.Headers, batch.Mapping, "City"),
                    State = Cell(row, batch.Headers, batch.Mapping, "State"),
                    PostalCode = Cell(row, batch.Headers, batch.Mapping, "PostalCode"),
                    Country = Cell(row, batch.Headers, batch.Mapping, "Country"),
                    Website = Cell(row, batch.Headers, batch.Mapping, "Website"),
                    LinkedInUrl = Cell(row, batch.Headers, batch.Mapping, "LinkedInUrl"),
                    FacebookUrl = Cell(row, batch.Headers, batch.Mapping, "FacebookUrl"),
                    WhatsAppNumber = Cell(row, batch.Headers, batch.Mapping, "WhatsAppNumber"),
                    Source = Cell(row, batch.Headers, batch.Mapping, "Source") ?? "csv-import",
                    Tags = Cell(row, batch.Headers, batch.Mapping, "Tags"),
                    Notes = Cell(row, batch.Headers, batch.Mapping, "Notes"),
                    CustomerType = Enum.TryParse<CustomerType>(Cell(row, batch.Headers, batch.Mapping, "CustomerType"), out var ct) ? ct : CustomerType.Lead,
                    CreatedById = _tenant.UserId
                };
                _db.Contacts.Add(contact);
                if (!string.IsNullOrEmpty(email)) existingEmails.Add(email);
                created++;
            }
            catch (Exception ex)
            {
                errors.Add($"Row: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Contact.Imported", "Contact", null, new { created, skipped, failed = errors.Count });
        cache.Remove($"import:{batchId}");

        TempData["Success"] = $"Import complete: {created} created, {skipped} skipped (duplicates/empty), {errors.Count} errors.";
        return RedirectToAction("Index", "Contacts");
    }

    private static string? Cell(string[] row, string[] headers, string?[] mapping, string field)
    {
        for (var i = 0; i < headers.Length && i < row.Length; i++)
            if (mapping[i] == field)
            {
                var v = row[i].Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
        return null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private sealed class ImportBatch
    {
        public required string[] Headers { get; init; }
        public required string?[] Mapping { get; init; }
        public required List<string[]> Rows { get; init; }
    }
}
