namespace Crm.Data;

/// <summary>Permission catalog. Format: "module.action".</summary>
public static class Permissions
{
    public const string ContactsView = "contacts.view";
    public const string ContactsCreate = "contacts.create";
    public const string ContactsEdit = "contacts.edit";
    public const string ContactsDelete = "contacts.delete";
    public const string ContactsExport = "contacts.export";
    public const string ContactsImport = "contacts.import";

    public const string AccountsView = "accounts.view";
    public const string AccountsCreate = "accounts.create";
    public const string AccountsEdit = "accounts.edit";
    public const string AccountsDelete = "accounts.delete";

    public const string LeadsView = "leads.view";
    public const string LeadsCreate = "leads.create";
    public const string LeadsEdit = "leads.edit";
    public const string LeadsDelete = "leads.delete";
    public const string LeadsAssign = "leads.assign";

    public const string DealsView = "deals.view";
    public const string DealsCreate = "deals.create";
    public const string DealsEdit = "deals.edit";
    public const string DealsDelete = "deals.delete";
    public const string DealsForecast = "deals.forecast";

    public const string ActivitiesView = "activities.view";
    public const string ActivitiesCreate = "activities.create";
    public const string ActivitiesEdit = "activities.edit";
    public const string ActivitiesDelete = "activities.delete";

    public const string TicketsView = "tickets.view";
    public const string TicketsCreate = "tickets.create";
    public const string TicketsEdit = "tickets.edit";
    public const string TicketsAssign = "tickets.assign";
    public const string TicketsDelete = "tickets.delete";

    public const string ProductsView = "products.view";
    public const string ProductsCreate = "products.create";
    public const string ProductsEdit = "products.edit";
    public const string ProductsDelete = "products.delete";

    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersEdit = "orders.edit";
    public const string OrdersDelete = "orders.delete";

    public const string InvoicesView = "invoices.view";
    public const string InvoicesCreate = "invoices.create";
    public const string InvoicesEdit = "invoices.edit";
    public const string InvoicesDelete = "invoices.delete";

    public const string CampaignsView = "campaigns.view";
    public const string CampaignsCreate = "campaigns.create";
    public const string CampaignsEdit = "campaigns.edit";
    public const string CampaignsDelete = "campaigns.delete";
    public const string CampaignsSend = "campaigns.send";

    public const string WorkflowsView = "workflows.view";
    public const string WorkflowsCreate = "workflows.create";
    public const string WorkflowsEdit = "workflows.edit";
    public const string WorkflowsDelete = "workflows.delete";
    public const string WorkflowsRun = "workflows.run";

    public const string IntegrationsView = "integrations.view";
    public const string IntegrationsCreate = "integrations.create";
    public const string IntegrationsEdit = "integrations.edit";
    public const string IntegrationsDelete = "integrations.delete";
    public const string IntegrationsSync = "integrations.sync";

    public const string AiView = "ai.view";
    public const string AiManage = "ai.manage";
    public const string AiUse = "ai.use";

    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";

    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";

    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public const string AuditView = "audit.view";
    public const string All = "*";

    public static readonly string[] AllPermissions =
    {
        ContactsView, ContactsCreate, ContactsEdit, ContactsDelete, ContactsExport, ContactsImport,
        AccountsView, AccountsCreate, AccountsEdit, AccountsDelete,
        LeadsView, LeadsCreate, LeadsEdit, LeadsDelete, LeadsAssign,
        DealsView, DealsCreate, DealsEdit, DealsDelete, DealsForecast,
        ActivitiesView, ActivitiesCreate, ActivitiesEdit, ActivitiesDelete,
        TicketsView, TicketsCreate, TicketsEdit, TicketsAssign, TicketsDelete,
        ProductsView, ProductsCreate, ProductsEdit, ProductsDelete,
        OrdersView, OrdersCreate, OrdersEdit, OrdersDelete,
        InvoicesView, InvoicesCreate, InvoicesEdit, InvoicesDelete,
        CampaignsView, CampaignsCreate, CampaignsEdit, CampaignsDelete, CampaignsSend,
        WorkflowsView, WorkflowsCreate, WorkflowsEdit, WorkflowsDelete, WorkflowsRun,
        IntegrationsView, IntegrationsCreate, IntegrationsEdit, IntegrationsDelete, IntegrationsSync,
        AiView, AiManage, AiUse,
        ReportsView, ReportsExport,
        SettingsView, SettingsManage,
        UsersView, UsersManage, RolesManage,
        AuditView
    };
}

/// <summary>Default role -> permission mapping. Stored as role claims at seed time.</summary>
public static class RolePermissions
{
    public const string SuperAdmin = "SuperAdmin";       // platform level, all tenants
    public const string OrgAdmin = "OrgAdmin";
    public const string SalesManager = "SalesManager";
    public const string SalesRep = "SalesRep";
    public const string MarketingManager = "MarketingManager";
    public const string MarketingRep = "MarketingRep";
    public const string SupportManager = "SupportManager";
    public const string SupportAgent = "SupportAgent";
    public const string FinanceManager = "FinanceManager";
    public const string FinanceStaff = "FinanceStaff";
    public const string Viewer = "Viewer";

    public static readonly string[] AllRoles =
    {
        OrgAdmin, SalesManager, SalesRep, MarketingManager, MarketingRep,
        SupportManager, SupportAgent, FinanceManager, FinanceStaff, Viewer
    };

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        OrgAdmin => Permissions.AllPermissions,
        SalesManager => new[]
        {
            Permissions.ContactsView, Permissions.ContactsCreate, Permissions.ContactsEdit, Permissions.ContactsExport, Permissions.ContactsImport,
            Permissions.AccountsView, Permissions.AccountsCreate, Permissions.AccountsEdit,
            Permissions.LeadsView, Permissions.LeadsCreate, Permissions.LeadsEdit, Permissions.LeadsDelete, Permissions.LeadsAssign,
            Permissions.DealsView, Permissions.DealsCreate, Permissions.DealsEdit, Permissions.DealsDelete, Permissions.DealsForecast,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate, Permissions.ActivitiesEdit, Permissions.ActivitiesDelete,
            Permissions.TicketsView, Permissions.TicketsCreate, Permissions.TicketsEdit,
            Permissions.OrdersView, Permissions.InvoicesView,
            Permissions.CampaignsView, Permissions.ReportsView, Permissions.ReportsExport,
            Permissions.AiView, Permissions.AiUse, Permissions.SettingsView, Permissions.UsersView
        },
        SalesRep => new[]
        {
            Permissions.ContactsView, Permissions.ContactsCreate, Permissions.ContactsEdit,
            Permissions.AccountsView,
            Permissions.LeadsView, Permissions.LeadsCreate, Permissions.LeadsEdit,
            Permissions.DealsView, Permissions.DealsCreate, Permissions.DealsEdit,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate, Permissions.ActivitiesEdit,
            Permissions.TicketsView, Permissions.TicketsCreate,
            Permissions.OrdersView, Permissions.InvoicesView,
            Permissions.AiView, Permissions.AiUse, Permissions.ReportsView
        },
        MarketingManager => new[]
        {
            Permissions.ContactsView, Permissions.ContactsCreate, Permissions.ContactsEdit, Permissions.ContactsExport,
            Permissions.LeadsView, Permissions.LeadsCreate, Permissions.LeadsEdit, Permissions.LeadsAssign,
            Permissions.CampaignsView, Permissions.CampaignsCreate, Permissions.CampaignsEdit, Permissions.CampaignsDelete, Permissions.CampaignsSend,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate,
            Permissions.DealsView, Permissions.ReportsView, Permissions.ReportsExport,
            Permissions.IntegrationsView, Permissions.IntegrationsSync,
            Permissions.AiView, Permissions.AiUse, Permissions.SettingsView
        },
        MarketingRep => new[]
        {
            Permissions.ContactsView, Permissions.LeadsView, Permissions.LeadsCreate, Permissions.LeadsEdit,
            Permissions.CampaignsView, Permissions.CampaignsCreate, Permissions.CampaignsEdit,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate,
            Permissions.DealsView, Permissions.AiView, Permissions.AiUse
        },
        SupportManager => new[]
        {
            Permissions.ContactsView, Permissions.AccountsView,
            Permissions.TicketsView, Permissions.TicketsCreate, Permissions.TicketsEdit, Permissions.TicketsAssign, Permissions.TicketsDelete,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate, Permissions.ActivitiesEdit,
            Permissions.DealsView, Permissions.OrdersView, Permissions.InvoicesView,
            Permissions.ReportsView, Permissions.AiView, Permissions.AiUse, Permissions.SettingsView
        },
        SupportAgent => new[]
        {
            Permissions.ContactsView, Permissions.AccountsView,
            Permissions.TicketsView, Permissions.TicketsCreate, Permissions.TicketsEdit,
            Permissions.ActivitiesView, Permissions.ActivitiesCreate, Permissions.ActivitiesEdit,
            Permissions.DealsView, Permissions.OrdersView, Permissions.InvoicesView,
            Permissions.AiView, Permissions.AiUse
        },
        FinanceManager => new[]
        {
            Permissions.ContactsView, Permissions.AccountsView,
            Permissions.DealsView, Permissions.DealsForecast,
            Permissions.OrdersView, Permissions.OrdersCreate, Permissions.OrdersEdit, Permissions.OrdersDelete,
            Permissions.InvoicesView, Permissions.InvoicesCreate, Permissions.InvoicesEdit, Permissions.InvoicesDelete,
            Permissions.ProductsView, Permissions.ProductsCreate, Permissions.ProductsEdit,
            Permissions.CampaignsView, Permissions.ReportsView, Permissions.ReportsExport,
            Permissions.IntegrationsView, Permissions.AiView, Permissions.AiUse, Permissions.SettingsView
        },
        FinanceStaff => new[]
        {
            Permissions.ContactsView, Permissions.AccountsView,
            Permissions.DealsView, Permissions.OrdersView, Permissions.OrdersCreate, Permissions.OrdersEdit,
            Permissions.InvoicesView, Permissions.InvoicesCreate, Permissions.InvoicesEdit,
            Permissions.ProductsView, Permissions.ReportsView, Permissions.AiView, Permissions.AiUse
        },
        Viewer => new[]
        {
            Permissions.ContactsView, Permissions.AccountsView, Permissions.LeadsView,
            Permissions.DealsView, Permissions.ActivitiesView, Permissions.TicketsView,
            Permissions.ProductsView, Permissions.OrdersView, Permissions.InvoicesView,
            Permissions.CampaignsView, Permissions.ReportsView, Permissions.AiView
        },
        _ => Array.Empty<string>()
    };
}
