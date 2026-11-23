# 🚀 Enterprise CRM

> A production-grade, multi-tenant **AI-powered CRM** — modular monolith built with ASP.NET Core 9 MVC, EF Core, and SQL Server. Standalone-ready with a visual integration engine, OAuth2, payments, vector RAG, and workflow automation.

![.NET](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-latest-239120?logo=csharp)
![EF Core](https://img.shields.io/badge/EF%20Core-9-5105BF?logo=dotnet)
![SQL Server](https://img.shields.io/badge/SQL%20Server-Express-CC2927?logo=microsoftsqlserver)
![License](https://img.shields.io/badge/license-MIT-green)

---

## ✨ Features

| Module | What's inside |
|---|---|
| 🔐 **Auth & Tenancy** | ASP.NET Identity, lockout, TOTP 2FA, public self-signup (each signup = isolated org workspace), role + granular permission system |
| 👥 **CRM Core** | Contacts, Accounts, Leads (scoring + convert), Activities/Tasks, unified timelines, custom fields |
| 📊 **Sales** | Visual pipeline Kanban (drag & drop), forecasting, weighted pipeline, win-rate reports |
| 🔌 **Integrations** | Visual API builder — 9 auth modes (API key, Bearer, Basic, OAuth2, custom header…), custom headers/query/body, SSRF-guarded test connection, JSON structure explorer, field mapping → Contacts/Accounts/Leads, scheduled sync + sync logs, 9 templates |
| 🤖 **AI** | Provider config (OpenAI / Anthropic / Gemini / any OpenAI-compatible endpoint), encrypted keys, test button, assistants chat, **vector-embedding RAG** over knowledge base |
| 📚 **Knowledge Base** | Upload txt/md/csv/json/html docs → auto-chunk → embed → cosine-similarity retrieval |
| 🎫 **Helpdesk** | Tickets with SLA fields, replies, assignment, priorities |
| 📣 **Marketing** | Campaigns with real SMTP send, audience selection |
| ⚙️ **Workflows** | JSON step engine — tasks, notifications, lead updates, webhooks, delays; scheduled triggers |
| 🛒 **Commerce** | Products/services, orders, invoices |
| 💳 **Payments** | Real **Stripe** Checkout — encrypted keys, hosted payment page, HMAC-SHA256 webhook verification |
| 🔁 **Background Jobs** | Hosted service: scheduled integration syncs + workflow triggers every 60s |
| 📈 **Reports** | Dashboard KPIs, pipeline/forecast/revenue reports, CSV export |
| 📥 **Import/Export** | CSV import wizard (upload → preview → dedupe → commit), CSV exports everywhere |
| 🎨 **UI** | Centralized neumorphic design system (`css/`), dark-ready tokens, responsive, permission-aware sidebar with collapsible groups |
| 🔒 **Security** | Tenant isolation via EF global filters, encrypted secrets (Data Protection **keys stored in DB**), audit log, rate-limited auth, security headers, SSRF guard |

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Crm.Web (MVC)                        │
│  Controllers · Services · Views · wwwroot (design sys)  │
├─────────────────────────────────────────────────────────┤
│                     Crm.Data                            │
│  Entities · ApplicationDbContext (tenant filters)       │
│  Permissions · EF Migrations                            │
├─────────────────────────────────────────────────────────┤
│              SQL Server (per-tenant rows)                │
└─────────────────────────────────────────────────────────┘
```

**Modular monolith** — clean module boundaries inside one deployable app:
- `src/Crm.Data` — entities, DbContext, permissions, migrations
- `src/Crm.Web` — controllers/services/views per module
- `css/` — centralized design system (source of truth, mirrored to `wwwroot`)
- `tests/Crm.Tests` — xUnit test project

**Multi-tenancy:** every tenant entity carries `OrganizationId` with automatic EF global query filters — companies never see each other's data. New signups get a fully isolated workspace (org + admin + default pipeline).

---

## 🚀 Quick Start

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (or SDK 10 — projects target `net9.0`)
- SQL Server (Express works: `localhost\SQLEXPRESS`)

### Run locally
```bash
git clone https://github.com/BigSmoke4/CRM.git
cd CRM
dotnet restore
cd src/Crm.Web
dotnet ef database update        # creates schema (also auto-applies at startup)
dotnet run --urls http://localhost:5080
```

Open **http://localhost:5080** → you'll be redirected to **`/Setup`** (first-run wizard) or **`/Account/Register`** for self-signup.

### Demo data
Sign in → **Settings → Load demo data** — seeds contacts, accounts, leads, deals, tickets, products, orders, invoices, campaigns, workflows across every module (idempotent, once per org).

---

## ⚙️ Configuration

`src/Crm.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost\\SQLEXPRESS;Database=CrmDb;Trusted_Connection=True;..."
  },
  "Smtp": { "Host": "...", "Port": 587, "User": "...", "Pass": "..." }
}
```

| Secret | Where it's entered | Storage |
|---|---|---|
| Integration API keys/tokens | Integrations → edit connection | Encrypted (Data Protection, keys in DB) |
| OAuth2 client secrets | Integrations → OAuth section | Encrypted |
| AI provider keys | AI → Providers | Encrypted, masked after save |
| Stripe keys | Payments → Settings | Encrypted |
| SMTP password | `appsettings.json` or env vars | Config / env |

**Environment variables** override anything: `ConnectionStrings__Default`, `Smtp__Pass`, etc.

---

## 🔌 Integrations — how it works

1. **Integrations → New** → pick a template (ERP customers/products/orders/invoices, website contacts, e-commerce…) or start blank
2. Set URL, method, auth type, headers, query params, body
3. **Test connection** → status, duration, response preview + JSON structure explorer
4. **Field mapping** → map JSON paths to Contact/Account/Lead fields (with transforms)
5. **Sync now** or set a schedule (`every:30m`, `hourly`, `daily`) — background service runs it automatically
6. **Logs** → per-run status + per-record errors

**OAuth2:** pick Gmail/Google, Microsoft 365, Slack, or custom → enter your app's client ID/secret → **Connect** → browser consent → tokens stored encrypted with refresh support.

**SSRF protection:** outbound calls are blocked for private IPs, loopback, cloud metadata endpoints, and non-HTTP schemes.

---

## 🤖 AI & RAG

- **Providers:** OpenAI, Anthropic, Gemini, or any OpenAI-compatible endpoint (local models too) — paste key + model, hit **Test**
- **Assistants:** create bots with custom instructions → chat at `/Ai` with CRM context
- **Knowledge Base:** upload documents → auto-chunked → embedded via your provider's `/embeddings` → chat retrieves by **cosine similarity** (keyword fallback when no embedding provider configured)

---

## 💳 Payments (Stripe)

1. Create a Stripe account → get `pk_`/`sk_`/`whsec_` keys
2. **Payments → Settings** → paste keys (encrypted)
3. Create an invoice → **Pay** button → customer completes Stripe Checkout
4. Register webhook `https://your-host/Payments/Webhook` in Stripe dashboard → invoice auto-marks paid on `checkout.session.completed`

---

## 🛡️ Security model

- **Tenant isolation** — EF global query filters on every tenant entity; super-admin bypass
- **Permissions** — `Permissions.cs` catalog → role mapping → dynamic policy provider; nav + APIs permission-aware
- **Secrets** — never plaintext; Data Protection with **keys stored in the database** (shared-hosting safe)
- **Auth** — cookie auth, lockout, 2FA, rate-limited endpoints, antiforgery everywhere
- **Outbound** — `OutboundUrlGuard` blocks SSRF; all integration HTTP via `IntegrationHttpService`
- **Audit** — audit log for sensitive actions

---

## 📦 Publishing

```bash
dotnet publish src/Crm.Web -c Release -o publish
```

Deploy the folder to any ASP.NET Core 9 host (IIS, Kestrel, Docker, Azure, shared hosting). Migrations auto-apply on startup. For shared hosting, DataProtection keys live in the DB — no filesystem dependencies.

---

## 🧪 Tests

```bash
dotnet test tests/Crm.Tests
```

Pure/testable units: `JsonExplorer`, `IntegrationSyncService.Transform`, `OutboundUrlGuard`, permission mapping.

---

## 🗺️ Roadmap / known gaps

- [ ] Excel (.xlsx) import/export (CSV fully works)
- [ ] Durable job queue for multi-instance deployments (current scheduler is single-instance safe)
- [ ] Provider-specific OAuth scope tuning per integration type
- [ ] More report types (custom report builder)

---

## 📄 License

MIT — see [LICENSE](LICENSE).
