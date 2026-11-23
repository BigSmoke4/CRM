# Agent notes

## Build & run
- `dotnet build Crm.slnx` (SDK 10 installed; projects target net9.0, runtime 9.0.x present)
- `dotnet ef migrations add <Name> -p src/Crm.Data -s src/Crm.Web -o Migrations`
- `dotnet ef database update -p src/Crm.Data -s src/Crm.Web`
- Run: `dotnet run --project src/Crm.Web --urls http://localhost:5080`
- SQL Server: `localhost\SQLEXPRESS`, Windows auth, db `CrmDb` (see appsettings.json)

## Conventions
- Entities in `src/Crm.Data/Entities/*.cs`; all tenant entities implement `ITenantEntity`.
- Tenant isolation is automatic via EF global query filters keyed on `ITenantContext`
  (claims `org_id` / `is_superadmin` added by `PermissionClaimsTransformation`).
- Permissions: add constant in `Crm.Data/Permissions.cs`, map in `RolePermissions.ForRole`,
  then use `[Authorize(Policy = Permissions.X)]` — no policy registration needed
  (`PermissionPolicyProvider` resolves dotted names).
- Secrets: never store plaintext — use `ISecretProtector` (Data Protection).
- Outbound HTTP for integrations MUST go through `IntegrationHttpService` (SSRF guard).
- UI: reuse `css/core` + `css/modules` + `site.css`; classes: `.panel.neu`, `.app-table`,
  `.btn`, `.field/.input`, `.chip`, `.kpi`, `.detail-grid`, `.activity-feed`.
- Views get permission-aware nav automatically via `Can()` in `_Layout.cshtml`.

## Testing
`dotnet test tests/Crm.Tests` (xunit). Add tests for mapping/JSON/security logic —
`JsonExplorer`, `IntegrationSyncService.Transform`, `OutboundUrlGuard` are pure/testable.
