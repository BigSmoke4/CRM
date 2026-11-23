using Crm.Data;
using Crm.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

/// <summary>Creates a new organization workspace with its admin user and default pipeline. Shared by setup wizard and public signup.</summary>
public class WorkspaceProvisioner
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public WorkspaceProvisioner(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    { _db = db; _userManager = userManager; _roleManager = roleManager; }

    public async Task<ApplicationUser> CreateAsync(string orgName, string firstName, string lastName,
        string email, string password, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var org = new Organization
            {
                Name = orgName.Trim(),
                Slug = Slugify(orgName),
                Currency = "USD",
                TimeZone = "UTC"
            };
            _db.Organizations.Add(org);
            await _db.SaveChangesAsync(ct);

            var user = new ApplicationUser
            {
                UserName = email.Trim(),
                Email = email.Trim(),
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                OrganizationId = org.Id,
                EmailConfirmed = true,
                IsActive = true
            };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

            var adminRole = await _roleManager.FindByNameAsync(RolePermissions.OrgAdmin);
            if (adminRole != null)
                await _userManager.AddToRoleAsync(user, adminRole.Name!);

            var pipeline = new Pipeline { OrganizationId = org.Id, Name = "Sales Pipeline", IsDefault = true };
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Qualification", Probability = 10, SortOrder = 1 });
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Discovery", Probability = 25, SortOrder = 2 });
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Proposal", Probability = 50, SortOrder = 3 });
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Negotiation", Probability = 75, SortOrder = 4 });
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Closed Won", Type = DealStageType.Won, Probability = 100, SortOrder = 5 });
            pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Closed Lost", Type = DealStageType.Lost, Probability = 0, SortOrder = 6 });
            _db.Pipelines.Add(pipeline);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            user.Organization = org;
            return user;
        });
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
