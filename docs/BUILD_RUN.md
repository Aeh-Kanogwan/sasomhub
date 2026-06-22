# BUILD_RUN — คู่มือ build / migrate / run (เครื่องที่มี .NET 8 SDK)

> Runbook นี้สำหรับเครื่อง dev/CI ที่ **ติดตั้ง .NET 8 SDK แล้ว** ทำตามจากบนลงล่างได้ทันที
> เครื่องที่ author solution ไม่มี SDK จึงยังไม่เคย build/migrate — ขั้นตอนด้านล่างคือ first run จริง
>
> Source of truth ของ schema: [`../sql/schema.sql`](../sql/schema.sql) (32 tables) — migration ต้อง **diff** กับไฟล์นี้
> เอกสารอ้างอิง: [`../src/README.md`](../src/README.md) · [`./DB_Schema.md`](./DB_Schema.md) · [`./SRS_MVP.md`](./SRS_MVP.md)

ทุกคำสั่งรันจากโฟลเดอร์ `src/` (โฟลเดอร์ที่มี `Marketplace.sln`) เว้นแต่ระบุไว้เป็นอย่างอื่น

---

## 0. Prerequisites (ติดตั้งครั้งเดียว)

```bash
# 1) .NET 8 SDK — ตรวจว่ามี SDK 8.0.x
dotnet --info        # ดูบรรทัด ".NET SDKs installed" ต้องมี 8.0.xxx
# ถ้ายังไม่มี: ติดตั้งจาก https://dotnet.microsoft.com/download/dotnet/8.0  (เลือก SDK ไม่ใช่แค่ Runtime)

# 2) EF Core CLI tool (global) — ต้องเป็น major เดียวกับ EF Core ในโปรเจกต์ (8.x)
dotnet tool install --global dotnet-ef --version 8.*
# ถ้าเคยติดตั้งไว้แล้วแต่เป็นเวอร์ชันเก่า:
dotnet tool update --global dotnet-ef --version 8.*
dotnet ef --version   # ควรขึ้น 8.0.x

# 3) SQL Server / LocalDB
#    - Windows + Visual Studio: มักมี (localdb)\MSSQLLocalDB ติดมาแล้ว
sqllocaldb info                     # ดู instance ที่มี
sqllocaldb start MSSQLLocalDB       # start ถ้ายังไม่ run
#    - ถ้าไม่มี LocalDB: ตั้ง MARKETPLACE_CONNECTION ชี้ไป SQL Server ตัวอื่น (ดูข้อ 1)
```

> หมายเหตุ: ถ้า `dotnet tool install` แล้วเรียก `dotnet-ef` ไม่เจอ ให้เพิ่ม `~/.dotnet/tools`
> (Windows: `%USERPROFILE%\.dotnet\tools`) เข้า `PATH` แล้วเปิด shell ใหม่

---

## 1. Secrets & Connection string (อ่านก่อน build) — สำคัญด้านความปลอดภัย

**กฎเหล็ก: ห้ามมี secret ใน repo.** `appsettings.json` เก็บได้เฉพาะค่า dev ที่ไม่ลับ (LocalDB,
issuer/audience) เท่านั้น — JWT signing key และ connection string ของ prod มาจาก secret store เสมอ

ลำดับการ resolve connection string (`AddInfrastructure` ใน `Marketplace.Infrastructure/DependencyInjection.cs`):

1. **env var `MARKETPLACE_CONNECTION`** (precedence สูงสุด — ใช้ใน CI/CD, container, prod)
2. `ConnectionStrings:MarketplaceDb` (user-secrets > `appsettings.{Env}.json` > `appsettings.json`)

ค่า default ใน `appsettings.json` ของ Api/Web/Worker ตรงกันทั้งสามตัว:

```
Server=(localdb)\MSSQLLocalDB;Database=MarketplaceDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

### 1.1 JWT signing key (Api) → ตั้งผ่าน user-secrets

`Marketplace.Api` มี `UserSecretsId = marketplace-api-secrets` แล้ว `Program.cs` จะ **fail fast**
ถ้า environment ไม่ใช่ Development และ key หาย/เป็น placeholder/สั้นกว่า 32 ตัว (กัน HS256 key เดาได้
หลุดขึ้น prod) ใน Development ถ้าไม่ตั้งจะ fallback เป็น dev key ชั่วคราวให้ boot ได้

```bash
# สร้าง signing key สุ่ม >= 32 ตัว (ตัวอย่าง) แล้วเก็บใน user-secrets ของเครื่อง dev:
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project Marketplace.Api
# (Windows ไม่มี openssl: ใช้ PowerShell)
#   $k = [Convert]::ToBase64String((1..48 | % {Get-Random -Max 256}))
#   dotnet user-secrets set "Jwt:SigningKey" "$k" --project Marketplace.Api

# (ทางเลือก) override connection string เฉพาะเครื่องนี้โดยไม่แตะ appsettings:
dotnet user-secrets set "ConnectionStrings:MarketplaceDb" "Server=...;Database=MarketplaceDb;..." --project Marketplace.Api
```

> user-secrets เก็บนอก repo ที่ `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` (Windows) /
> `~/.microsoft/usersecrets/<id>/secrets.json` — ปลอดภัย ไม่ถูก commit
>
> **prod/staging**: อย่าใช้ user-secrets — ส่ง key ผ่าน env var แทน เช่น
> `Jwt__SigningKey` และ `MARKETPLACE_CONNECTION` (ดู mapping `:` → `__` ของ .NET configuration)

### 1.2 Web (cookie auth) — ไม่มี JWT key

`Marketplace.Web` ใช้ cookie auth ไม่ต้องตั้ง signing key มี `UserSecretsId = marketplace-web-secrets`
ไว้สำหรับ override connection string เฉพาะเครื่องเท่านั้น (optional)

---

## 2. Restore & Build (ทั้ง solution)

```bash
cd src
dotnet restore Marketplace.sln
dotnet build Marketplace.sln -c Debug
```

ลำดับ build (project dependency): `Domain` → `Application` → `Infrastructure` → `Api`/`Web`/`Worker` → `Tests`
`dotnet build` แก้ลำดับให้เองจาก ProjectReference

**ข้อควรระวัง transitive package (first restore บนเครื่องที่ไม่เคย build):**

- **`Microsoft.Data.SqlClient`** — มาทาง `Microsoft.EntityFrameworkCore.SqlServer 8.0.8` (transitive)
  ไม่ต้องอ้างตรง ถ้า build error เรื่อง SqlClient/SNI ให้ตรวจว่า restore ดึง runtime-specific asset
  ครบ (เครื่อง offline/proxy อาจดึงไม่ครบ) — แก้ด้วย `dotnet restore --force` ออนไลน์ครั้งแรก
- **`Microsoft.Extensions.Logging.Abstractions`** — service classes (`TrustScoreService` ฯลฯ) ใช้
  `ILogger<T>` ซึ่งมาทาง EF Core / Hosting transitive ไม่ต้องเพิ่ม PackageReference เอง
  ถ้าเจอ version conflict (NU1605 downgrade) ให้ align ทุก `Microsoft.Extensions.*` / `Microsoft.EntityFrameworkCore.*`
  เป็น `8.0.x` เดียวกัน (ตอนนี้ทุก csproj ใช้ 8.0.8 / 8.0.0 อยู่แล้ว)
- เวอร์ชันที่ pin ไว้ตอนนี้: EF Core 8.0.8, JwtBearer 8.0.8, Swashbuckle 6.6.2, xunit 2.9.0,
  EFCore.InMemory 8.0.8 — อย่าผสม EF Core 9.x เด็ดขาด (target คือ net8.0)

---

## 3. Database & Migration

มี **2 ทางเลือก** เลือกอย่างใดอย่างหนึ่ง — schema.sql คือ source of truth เสมอ

### ทางเลือก A (แนะนำสำหรับ prod / parity สูงสุด): รัน `schema.sql` ตรง แล้ว stamp migration

`schema.sql` มี features ที่ EF model อาจ generate ไม่ครบ/ไม่ตรง:
filtered unique index (`WHERE IsDeleted = 0`, idempotency), `CHECK` constraints,
`NEWSEQUENTIALID()` defaults, `ROWVERSION`, และ **append-only INSTEAD OF triggers**
(`TR_AuditLogs_NoModify` / `TR_ConsentRecords_NoModify` / `TR_CreditTransactions_NoModify`)

```bash
# 1) สร้าง DB + schema จาก source of truth (ต้องมี sqlcmd หรือใช้ Azure Data Studio / SSMS)
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('MarketplaceDb') IS NULL CREATE DATABASE MarketplaceDb;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d MarketplaceDb -i ..\sql\schema.sql

# 2) สร้าง migration ไว้ในโค้ด (เพื่อให้ history/snapshot ตรงกับโมเดล) แต่ "อย่า" update DB
dotnet ef migrations add InitialCreate \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api \
  --output-dir Migrations

# 3) stamp ว่า migration นี้ apply แล้ว (เขียนแถวลง __EFMigrationsHistory โดยไม่รัน DDL)
#    หมายเหตุ: dotnet-ef 8 ไม่มีคำสั่ง "stamp" ตรง ๆ — ใช้ SQL ใส่แถวเองตาม MigrationId ที่ได้จากข้อ 2
#    (MigrationId = ชื่อโฟลเดอร์ใน Migrations/ เช่น 20260618xxxxxx_InitialCreate)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d MarketplaceDb -Q "IF OBJECT_ID('__EFMigrationsHistory') IS NULL CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL); INSERT INTO __EFMigrationsHistory VALUES (N'<MIGRATION_ID>', N'8.0.8');"
```

### ทางเลือก B (สะดวกสำหรับ dev local): ให้ EF สร้าง DB จาก migration

```bash
dotnet ef migrations add InitialCreate \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api \
  --output-dir Migrations
```

**ก่อน `database update` ต้อง DIFF migration กับ `schema.sql`** — ตรวจว่า EF generate ครบ:

```bash
# ดู SQL ที่ migration จะรัน (ไม่แตะ DB) เพื่อเทียบกับ schema.sql
dotnet ef migrations script \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api \
  --output InitialCreate.sql
```

checklist diff (ถ้าขาด ให้เติมใน `IEntityTypeConfiguration` หรือใช้ทางเลือก A):

- [ ] filtered unique index: `UX_Users_NormalizedEmail WHERE IsDeleted=0`,
      `UX_Membership_LivePerUser WHERE Status IN ('Trial','Active')`,
      `UX_CreditTx_Idempotency WHERE IdempotencyKey IS NOT NULL`,
      `UX_CreditTx_TypeRef WHERE RefId IS NOT NULL`,
      `UX_Notif_NoDup` — **สำคัญต่อ idempotency / no double-credit**
- [ ] `CHECK` constraints ทั้งหมด (status enums, `BuyerId <> SellerId`, `Balance >= 0`, ฯลฯ)
- [ ] `NEWSEQUENTIALID()` defaults บน Guid PK (ลด index fragmentation)
- [ ] `ROWVERSION` (optimistic concurrency) บน Users/Memberships/Products/Auctions/Transactions/TrustScores/CreditAccounts
- [ ] **append-only triggers** บน AuditLogs / ConsentRecords / CreditTransactions
      (EF migration **ไม่ generate trigger ให้** — ต้องเพิ่ม raw SQL ใน migration ด้วย `migrationBuilder.Sql(...)`
      หรือใช้ทางเลือก A)

ถ้า diff ผ่านแล้วค่อย apply:

```bash
dotnet ef database update \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api
```

> ⚠️ ถ้าใช้ทางเลือก B ต้องเติม trigger เองด้วย `migrationBuilder.Sql(...)` ไม่งั้น append-only
> (FR-24/FR-29, AML/PDPA) จะไม่ถูก enforce ที่ระดับ DB — ถือเป็น regression ด้านกฎหมาย

---

## 4. Seed data (lookup + config) — ต้องทำก่อนทดสอบ flow ธุรกิจ

### 4.1 Lookup ที่ seed อัตโนมัติแล้ว (ผ่าน `HasData` / schema.sql)

`KycStatuses`, `MembershipTiers` (Normal=1000/Verified=1500/Premium=2000), `PromotionPackages`,
`BlacklistReasonCodes` **1–6**, `Categories` 1–5 — ทั้งคู่ (migration และ schema.sql) ตรงกัน

### 4.2 ต้อง seed เพิ่มด้วยมือ (service ต้องใช้ มิฉะนั้น fallback / no-op)

**(a) `BlacklistReasonCode` Id 7 = `COMPLETED_DEAL`** — `TradeService.TryRewardCompletedDealAsync`
ใช้ ReasonCodeId 7 เพื่อให้ +5 trust แก่ทั้ง buyer/seller เมื่อปิดดีล ถ้าไม่มีแถวนี้ จะ log debug
แล้ว **ข้ามการให้รางวัล** (fail soft — ไม่บล็อกการยืนยันรับของ)

```sql
INSERT INTO dbo.BlacklistReasonCodes (ReasonCodeId, Code, DisplayName, Severity, DefaultScorePenalty, IsActive)
VALUES (7, 'COMPLETED_DEAL', N'Completed deal', 1, 5, 1);
```
> หมายเหตุ schema: ตาราง `BlacklistReasonCodes` เป็น reason ทั่วไป (ใช้ทั้ง penalty และ trust event)
> Severity ต้อง 1–5 (CK), DefaultScorePenalty = **+5** (positive = reward)

**(b) `ConfigVersions`** — ราคา/รางวัล/expiry resolve ผ่าน `ConfigVersionResolver`
ถ้าไม่ seed: membership ใช้ fallback = `MembershipTiers.AnnualPriceTHB`, trial = 3 เดือน (default ในโค้ด)
แต่ **referral reward fallback = 0** → ไม่มี seed = ผู้แนะนำ/ผู้ถูกแนะนำ **ได้ credit 0** (เงียบ ไม่ error)
จึงควร seed อย่างน้อยชุดนี้:

```sql
INSERT INTO dbo.ConfigVersions (ConfigKey, Value, EffectiveFromUtc, Note) VALUES
  ('MembershipTier.Normal.AnnualPriceTHB',   '1000', '2020-01-01T00:00:00', N'seed baseline'),
  ('MembershipTier.Verified.AnnualPriceTHB', '1500', '2020-01-01T00:00:00', N'seed baseline'),
  ('MembershipTier.Premium.AnnualPriceTHB',  '2000', '2020-01-01T00:00:00', N'seed baseline'),
  ('Membership.TrialMonths',                 '3',    '2020-01-01T00:00:00', N'seed baseline'),
  ('Referral.RewardCreditToReferrer',        '100',  '2020-01-01T00:00:00', N'seed baseline (ปรับตามนโยบายจริง)'),
  ('Referral.RewardCreditToReferred',        '50',   '2020-01-01T00:00:00', N'seed baseline (ปรับตามนโยบายจริง)'),
  ('Referral.CreditExpiryDays',              '180',  '2020-01-01T00:00:00', N'seed baseline');
```
> `EffectiveFromUtc` ตั้งเป็นอดีตเพื่อให้ resolve เป็น "ค่าปัจจุบัน" ได้ทันที (resolver = latest EffectiveFromUtc <= now)
> ค่ารางวัล referral เป็น baseline ทางเทคนิค — **ต้องให้ทีม business/legal ยืนยันตัวเลขจริง** ก่อนใช้งานจริง

---

## 5. Run (Web / Api / Worker) + Test

```bash
# Web (MVC, cookie auth) — server-rendered site
dotnet run --project Marketplace.Web
#   https://localhost:xxxx (ดูพอร์ตจาก launchSettings / console)

# Api (Swagger ที่ /swagger เฉพาะ Development) — ต้องตั้ง Jwt:SigningKey ก่อน (ข้อ 1.1) ถ้ารัน Production
dotnet run --project Marketplace.Api

# Worker (BackgroundServices: AuctionCloser / TrustScoreRecalc / MembershipTrialExpiry / NotificationDispatch)
dotnet run --project Marketplace.Worker

# Unit tests (InMemory provider)
dotnet test Marketplace.sln
```

> Worker/Api/Web ใช้ connection string เดียวกัน (ข้อ 1) — เปิดพร้อมกันชี้ DB เดียวได้

---

## 6. Verify checklist (บน SQL Server จริง — InMemory test ครอบไม่ถึง)

`dotnet test` ใช้ EF Core **InMemory** ซึ่ง **ไม่ enforce**: unique/filtered index, CHECK,
triggers, ROWVERSION concurrency จึงต้อง verify ของจริงเพิ่มหลัง `database update`:

- [ ] **Append-only triggers**: `UPDATE dbo.AuditLogs SET Action='x';` และ `DELETE` ต้อง **THROW 51001**
      (เช่นเดียวกับ ConsentRecords=51002, CreditTransactions=51003)
- [ ] **Idempotency (no double-credit)**: insert `CreditTransactions` สอง row ที่ `IdempotencyKey` เดียวกัน
      ต้องชน `UX_CreditTx_Idempotency`; insert ซ้ำ `(Type, RefId)` เดียวกันต้องชน `UX_CreditTx_TypeRef`
- [ ] **One live membership per user**: insert membership Trial/Active row ที่สองของ user เดิม ต้องชน `UX_Membership_LivePerUser`
- [ ] **Soft-delete email uniqueness**: email ซ้ำได้ถ้า row เดิม `IsDeleted=1`; ซ้ำกับ row `IsDeleted=0` ต้องชน `UX_Users_NormalizedEmail`
- [ ] **CHECK**: insert `Transactions` ที่ `BuyerId = SellerId` ต้องถูกปฏิเสธ (CK_Tx_Parties); `Balance < 0` ถูกปฏิเสธ (CK_CreditAcc_Balance)
- [ ] **Trial must have end**: membership Status='Trial' ที่ `TrialEndsAtUtc IS NULL` ต้องถูกปฏิเสธ (CK_Membership_TrialHasEnd)
- [ ] **ROWVERSION concurrency**: แก้ row เดียวกันสองครั้งด้วย stale rowversion → ครั้งที่สอง `DbUpdateConcurrencyException`
- [ ] **COMPLETED_DEAL reward**: หลัง seed ReasonCodeId 7 ปิดดีลแล้ว trust ของ buyer+seller ขยับ +5 และมีแถว `TrustScoreHistory`
- [ ] **Referral reward**: หลัง seed ConfigVersions ทำ referral flow แล้วได้ credit > 0 (ไม่ใช่ 0)
- [ ] **JWT fail-fast**: ตั้ง `ASPNETCORE_ENVIRONMENT=Production` แล้วรัน Api โดยไม่มี `Jwt:SigningKey` → ต้อง throw ตอน startup

---

## 7. Deploy notes (สำหรับ staging/prod — ฝัง security)

- **Always Encrypted สำหรับ `dbo.KycSensitiveData`** (Legal #2 / PDPA): คอลัมน์ `FullNameMasked`,
  `NationalIdHash` ต้อง bind CMK/CEK ที่ deploy `schema.sql` ใช้ plain type ให้รันได้ทันที ตอน deploy จริง:
  1. สร้าง Column Master Key (CMK) ใน key store (Windows Cert Store / Azure Key Vault)
  2. สร้าง Column Encryption Key (CEK) แล้ว `ALTER TABLE dbo.KycSensitiveData ALTER COLUMN ... ENCRYPTED WITH (...)`
     (deterministic สำหรับคอลัมน์ที่ต้อง query/JOIN, randomized สำหรับที่เก็บเฉย ๆ)
  3. connection string ฝั่ง app เพิ่ม `Column Encryption Setting=Enabled`
  4. **ห้ามเก็บภาพบัตร/บัญชีดิบ** — เก็บแค่ status + provider ref (`KycVerifications`) และ
     encrypted/masked/ hash ใน `KycSensitiveData` พร้อม `RetentionExpiresAtUtc` (retention-bound)
- **Secrets ใน prod**: `Jwt__SigningKey`, `MARKETPLACE_CONNECTION` ผ่าน env var/key vault — ไม่ใช่ user-secrets, ไม่ใช่ appsettings
- **HTTPS/HSTS**: Web เปิด `UseHsts()` + `UseHttpsRedirection()` นอก Development แล้ว cookie `SecurePolicy=SameAsRequest`
  จะกลายเป็น Secure จริงเมื่ออยู่หลัง HTTPS — prod ต้อง terminate TLS และส่ง traffic เป็น https ถึง app (หรือตั้ง ForwardedHeaders)
- **DB least-privilege** (defence-in-depth เสริม trigger): `DENY UPDATE, DELETE ON dbo.AuditLogs / dbo.ConsentRecords / dbo.CreditTransactions TO [app_role];`
- **Backup/rollback**: backup DB ก่อนทุก migration; เก็บ `dotnet ef migrations script <from> <to>` ไว้เป็น forward + เตรียม `migrations script <to> <from>` เป็น rollback

---

## 8. Troubleshooting

| อาการ | สาเหตุ/วิธีแก้ |
|---|---|
| `No connection string...` ตอน startup | ไม่มีทั้ง `MARKETPLACE_CONNECTION` และ `ConnectionStrings:MarketplaceDb` — ตั้งอย่างใดอย่างหนึ่ง |
| Api throw `Jwt:SigningKey is missing...` | รันนอก Development โดยไม่มี key — `dotnet user-secrets set "Jwt:SigningKey" ...` หรือ env `Jwt__SigningKey` |
| `dotnet ef` not found | `dotnet tool install --global dotnet-ef --version 8.*` แล้วเพิ่ม `~/.dotnet/tools` ใน PATH |
| migration ไม่มี trigger/filtered index | EF ไม่ generate ให้ — ใช้ทางเลือก A (รัน schema.sql) หรือเติม `migrationBuilder.Sql(...)` |
| NU1605 package downgrade | align ทุก `Microsoft.EntityFrameworkCore.*` / `Microsoft.Extensions.*` เป็น 8.0.x |
| LocalDB ต่อไม่ได้ | `sqllocaldb start MSSQLLocalDB` หรือชี้ `MARKETPLACE_CONNECTION` ไป SQL Server อื่น |
