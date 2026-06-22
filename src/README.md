# Marketplace (Collectibles) — .NET 8 Solution

Source of truth: [`../sql/schema.sql`](../sql/schema.sql) (32 tables) · [`../docs/DB_Schema.md`](../docs/DB_Schema.md) · [`../docs/SRS_MVP.md`](../docs/SRS_MVP.md)

Stack: **.NET 8 Web API + EF Core 8 (Code-First) + SQL Server + Worker/BackgroundService**, Clean-ish architecture.

## Projects

| Project | Type | Responsibility | Depends on |
|---|---|---|---|
| **Marketplace.Domain** | classlib | 30 entity POCOs (32 tables) + enums. **No dependencies.** | — |
| **Marketplace.Application** | classlib | Service interfaces + DTOs (skeleton): `IMembershipService`, `ICreditService`, `IAuctionService`, `IReferralService` | Domain |
| **Marketplace.Infrastructure** | classlib | `MarketplaceDbContext`, one `IEntityTypeConfiguration` per entity, lookup seed, DI, design-time factory | Domain, Application |
| **Marketplace.Api** | web | ASP.NET Core Web API: `Program.cs` (DbContext, JWT placeholder, Swagger) + skeleton controllers | Application, Infrastructure |
| **Marketplace.Worker** | worker | BackgroundServices: `AuctionCloserService`, `TrustScoreRecalcService`, `MembershipTrialExpiryService` | Application, Infrastructure |

## Legal-driven design (must not regress)

- **No-touch payment**: there is **no wallet/balance/escrow/ledger of trade money** anywhere. `Transactions` only records an off-platform direct transfer. Platform revenue lives in `FeeInvoices` (company money, separate).
- **Credit is non-cashable**: `CreditAccounts`/`CreditTransactions` are loyalty points spendable on platform services only. No Withdraw/CashOut/Transfer type/column/endpoint exists by design.
- **Referral is single-level**: `Referrals` has no upline/downline/level column. Rewards paid in credit, never cash.
- **KYC**: stores status + provider ref only; sensitive data (encrypted, retention-bound) is isolated in `KycSensitiveData`.
- **Blacklist**: internal warn-on-deal with standard reason codes + due process (ReviewStatus/AppealStatus/ExpiresAt).
- **PDPA**: `ConsentRecords` + `AuditLogs` (append-only); soft-delete/anonymize on `Users`.

## Prerequisites

- .NET 8 SDK
- SQL Server / LocalDB (default connection string targets `(localdb)\MSSQLLocalDB`, DB `MarketplaceDb`)
- EF Core tools: `dotnet tool install --global dotnet-ef`

Connection string lives in `Marketplace.Api`/`Marketplace.Web`/`Marketplace.Worker` `appsettings.json`
(`ConnectionStrings:MarketplaceDb`, all three identical: LocalDB `MarketplaceDb`). `AddInfrastructure`
and the design-time factory both honor the `MARKETPLACE_CONNECTION` env var first (CI/CD / prod override).

**Secrets**: the JWT signing key is **not** in `appsettings.json` — set it via user-secrets in dev
(`dotnet user-secrets set "Jwt:SigningKey" ... --project Marketplace.Api`) or env var `Jwt__SigningKey`
in prod. The Api fails fast outside Development if the key is missing/placeholder/<32 chars.

> Full step-by-step build/migrate/seed/run + security verify checklist: [`../docs/BUILD_RUN.md`](../docs/BUILD_RUN.md).

## Build

```bash
cd src
dotnet restore
dotnet build
```

## Create & apply the initial migration

The migration was NOT generated yet (no .NET SDK was available on the authoring machine).
Run from the `src` folder:

```bash
# 1) create the migration (lives in Marketplace.Infrastructure/Migrations)
dotnet ef migrations add InitialCreate \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api \
  --output-dir Migrations

# 2) apply to the database
dotnet ef database update \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api
```

> Note: the project intends to use **the existing `sql/schema.sql` as the source of truth**.
> After generating `InitialCreate`, diff it against `schema.sql` (filtered indexes, CHECK
> constraints, `NEWSEQUENTIALID()` defaults, `ROWVERSION`) and reconcile any drift before
> the first `database update`. Alternatively, run `sql/schema.sql` directly and use the
> migration only to stamp `__EFMigrationsHistory`.

## Run

```bash
# API (Swagger at /swagger)
dotnet run --project Marketplace.Api

# Worker
dotnet run --project Marketplace.Worker
```

## TODO for the dev team

See the in-code `// TODO` markers (controllers + services + workers). High level:
- Implement `IMembershipService` / `ICreditService` / `IAuctionService` / `IReferralService` in Infrastructure and register them in DI.
- Real JWT issuance + refresh + RBAC (NFR-S1/S3). (Signing key already moved to user-secrets/env — see docs/BUILD_RUN.md §1.)
- Membership-active gate on listing/bid endpoints (FR-06/FR-10).
- Anti-shill detection (FR-11), auction close + no-touch transaction creation (FR-12/FR-18).
- Credit grant/spend/expiry + single-level referral payout (FR-28/29/30).
- Worker bodies: reminders 14/3/1 days, expiry flips, trust recalc, credit/promotion expiry sweeps.
- Always Encrypted binding for `KycSensitiveData` columns at deploy.
