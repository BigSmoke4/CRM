# Enterprise CRM

ASP.NET Core 9 MVC + EF Core + SQL Server (multi-tenant modular monolith).

## Stack
- Backend: ASP.NET Core 9 MVC, C# (net9.0)
- Data: EF Core 9 + SQL Server (tested on `localhost\SQLEXPRESS`)
- Auth: ASP.NET Identity (cookie auth, lockout, TOTP 2FA, role + permission claims)
- Design system: `css/core` + `css/modules` (neumorphic soft-UI), copied to `src/Crm.Web/wwwroot/css`

## Layout
```
src/Crm.Data    Entities, ApplicationDbContext (tenant query filters), Permissions, migrations
src/Crm.Web     MVC app: Controllers per module, Services, ViewModels, Views
css/            Centralized design system (source of truth; mirrored into wwwroot)
tests/Crm.Tests xUnit test project
docs/           Architecture + ops notes
```

## Run
```
dotnet restore
cd src/Crm.Web
dotnet ef database update          # creates CrmDb on first run (migrations auto-apply at startup too)
dotnet run --urls http://localhost:5080
```
First request to `/` redirects to `/Setup` (wizard) when no organization exists.

## Configuration
`appsettings.json` → `ConnectionStrings:Default` (SQL Server). Secrets for integrations and
AI keys are encrypted at rest via ASP.NET Core Data Protection (key ring in `.keys/` at repo root,
override with `DataProtection:KeyRingPath`). SMTP for campaigns: `Smtp:*` config keys.

## Modules implemented
Setup wizard · Auth (login/2FA/lockout) · Dashboard KPIs · Contacts/Accounts/Leads (CRUD,
CSV import/export, lead scoring, convert-to-contact) · Activities/Tasks · Kanban deals +
forecast · Tickets (SLA fields, replies, assignment) · Campaigns (SMTP send when configured) ·
Workflows (JSON step engine: tasks, notifications, lead status, webhooks, delay) ·
Commerce (products, orders, invoices) · Users/roles · Settings (org, custom fields, pipeline
stages, audit log) · Reports + CSV export · **Integrations** (visual API builder: auth types,
custom headers/query/body, SSRF-guarded test connection, JSON structure explorer, field
mapping to Contact/Account/Lead, sync engine with logs and per-record errors, templates) ·
**AI** (provider config for OpenAI/Anthropic/Gemini/OpenAI-compatible, test connection,
assistants chat with RAG keyword retrieval over indexed knowledge docs).

## Security notes
- Every tenant entity has `OrganizationId` + EF global query filter (super-admin bypass).
- Permission claims computed per request by `PermissionClaimsTransformation`;
  policies resolved dynamically by `PermissionPolicyProvider`.
- Integration secrets encrypted via `ISecretProtector` (Data Protection); masked in UI.
- Outbound integration calls go through `OutboundUrlGuard` (blocks private IPs, loopback,
  link-local/metadata endpoints, non-http schemes).
- Antiforgery auto-validated; security headers + rate limiting on auth endpoints.

## Known limitations / next steps
- AI RAG uses keyword retrieval; embedding/vector search is the upgrade path
  (`KnowledgeChunk.EmbeddingJson` column already exists).
- Workflow triggers fire via `WorkflowEngine.RunAsync` (manual + can be invoked from
  record events); a hosted `BackgroundService` scheduler for sync schedules is the next step.
- OAuth2 flows for Gmail/M365/etc. need provider app registration; bearer/API-key/custom-header
  auth works today.
- Excel (.xlsx) import: CSV supported now; add ClosedXML/EPPlus reader for .xlsx.
- Payment processing intentionally not faked — wire a real provider (Stripe) when needed.
