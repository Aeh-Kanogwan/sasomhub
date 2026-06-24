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

### 1.3 รายการ secret ที่ต้องตั้งใน prod (env var / key vault) — checklist

ทุกตัวส่งผ่าน **env var** (mapping `:` → `__`) หรือ key vault — **ห้าม** ใส่ใน `appsettings.json` หรือ user-secrets บน prod
ตอนนี้ใน repo `appsettings.json` มีแต่ค่า dev/placeholder/ว่าง (ตรวจแล้ว ไม่มี secret หลุด — ดู §1.4)

| Secret (env var) | ใช้ที่ไหน | บังคับบน prod? | หมายเหตุ |
|---|---|---|---|
| `MARKETPLACE_CONNECTION` | Api/Web/Worker | **ใช่** | connection string จริง (precedence สูงสุด) |
| `Jwt__SigningKey` | Api | **ใช่** | HS256 ≥32 ตัว, สุ่ม; Api fail-fast ถ้าหาย/placeholder/สั้น |
| `Kyc__Mode` | Api/Worker | **ใช่** | ต้อง `Ndid` บน prod — `Mock` boot ไม่ได้ (LEGAL #2 hard-guard) |
| `Kyc__NdidApiKey` | KYC transport | ใช่ (เมื่อ Ndid) | key ของ NDID gateway |
| `Kyc__NdidRelyingPartyId` | KYC transport | ใช่ (เมื่อ Ndid) | RP id |
| `Kyc__NdidBaseUrl` | KYC transport | ใช่ (เมื่อ Ndid) | endpoint NDID (sandbox/prod) — ยังเป็น TODO ต้องใส่ค่าจริง |
| `Kyc__NationalIdHashPepper` | KYC | **ใช่** | pepper สำหรับ hash เลขบัตร (อย่าใช้ค่า dev) |
| `Notifications__RecipientHashPepper` | Notification | **ใช่** | pepper HMAC ผู้รับ; dev default = `dev-only-...-change-me` ต้องเปลี่ยน |
| `Notifications__Smtp__Username` / `__Password` | SMTP sender | ถ้าใช้ SMTP | บัญชี relay |
| `Notifications__SendGrid__ApiKey` | SendGrid sender | ถ้าใช้ SendGrid | |
| `Notifications__Sms__ApiKey` (+ `__Url`) | SMS gateway | ถ้าใช้ SMS | |

> **prod guard (LEGAL #2):** `ModuleRegistration.AddM2Kyc` โยน exception ตอน startup ถ้า
> `ASPNETCORE_ENVIRONMENT=Production` แต่ `Kyc:Mode != Ndid` → host บน prod **ห้าม** รันด้วย Mock provider
> (กัน sandbox identity-proofing หลุดขึ้น prod) — ยืนยันด้วยการตั้ง `Kyc__Mode=Ndid` ก่อน deploy

### 1.4 ยืนยันไม่มี secret หลุดใน repo

`appsettings.json` ของ Api/Web/Worker เก็บเฉพาะ: connection string dev (LocalDB), issuer/audience,
provider mode (`Log`/`Mock`), และ pepper **dev-only ที่มีคำว่า `change-me`** — ฟิลด์ secret จริง
(`NdidApiKey`, `NationalIdHashPepper`, SMTP/SendGrid/SMS keys) เป็น **string ว่าง** พร้อม `_NOTE`
สั่งให้ตั้งผ่าน secret store; `Jwt:SigningKey` **ไม่มี** ใน appsettings เลย (มาจาก user-secrets/env เท่านั้น)

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

> **สรุปสั้น:** มี **2 ทางเลือก** ที่ "convergent" — ทั้งคู่ติดตั้ง append-only triggers ครบ
> เพราะ trigger ถูกบรรจุไว้ทั้งใน `sql/triggers.sql` (idempotent, `CREATE OR ALTER`) **และ**
> ใน EF migration `20260624063844_AddAppendOnlyTriggers` (รัน raw SQL ผ่าน `migrationBuilder.Sql`)
> schema.sql ยังเป็น source of truth ของ DDL เสมอ
>
> **ไฟล์ deploy ที่เกี่ยว:** `sql/schema.sql` (33 tables + index + CHECK + seed),
> `sql/triggers.sql` (4 triggers, idempotent), `sql/seed_dev.sql` (dev seed), `sql/deploy.ps1` (orchestrator)

### ทางเลือก A (แนะนำสำหรับ prod / parity สูงสุด): **คำสั่งเดียว** `deploy.ps1`

`deploy.ps1` ทำครบในคำสั่งเดียว แบบ idempotent + reproducible: create DB → run `schema.sql`
→ run `triggers.sql` → run `seed_dev.sql` (ข้ามถ้า `-Prod`) → **stamp `__EFMigrationsHistory`
ทุก migration ที่พบในโฟลเดอร์ Migrations อัตโนมัติ** (อ่านจากไฟล์จริง ไม่ hard-code จึงไม่ล้าสมัย)
→ verify ว่ามี 4 triggers ครบ ไม่ต้องจำ tribal knowledge เรื่อง stamp/`sqlcmd -I` อีกต่อไป

```powershell
# fresh dev DB (localhost, default instance) — คำสั่งเดียวจบ
pwsh sql/deploy.ps1
#   (Windows PowerShell 5.1: powershell.exe -NoProfile -ExecutionPolicy Bypass -File sql\deploy.ps1)

# สร้าง DB ทดสอบใหม่จากศูนย์ (ไม่แตะ dev data)
pwsh sql/deploy.ps1 -Database MarketplaceDb_Verify -DropFirst

# prod/staging (ข้าม dev seed; ตั้ง ConfigVersions/บัญชีบริษัทจริงเอง)
pwsh sql/deploy.ps1 -Server prod-sql01 -Database MarketplaceDb -Prod
```

> `deploy.ps1` ใช้ `sqlcmd -E` (Trusted_Connection) + `-I -C -b`. ถ้า prod ต้องใช้ SQL auth
> ให้รัน 3 ไฟล์ตรงด้วย `sqlcmd -U <user> -P <pwd> -C -I -b -d <db> -i sql\schema.sql` (แล้ว triggers/seed)
> — CI (Linux + SA) ก็ทำแบบนี้ ดู `.github/workflows/ci.yml` job `integration`
>
> `sqlcmd -I` (QUOTED_IDENTIFIER ON) **บังคับ** ไม่งั้น filtered index ล้ม — deploy.ps1 ตั้งให้แล้ว

### ทางเลือก B (dev local / forward migrate): `dotnet ef database update`

ตอนนี้ EF chain ติดตั้ง trigger ครบแล้ว (ผ่าน migration `AddAppendOnlyTriggers`) — ไม่ต้องเติม manual

```bash
dotnet ef database update \
  --project Marketplace.Infrastructure \
  --startup-project Marketplace.Api
```

> ✅ ตรวจแล้ว (DevSecOps): `dotnet ef database update` บน DB ว่าง → ลง 6 migrations ครบรวม
> `AddAppendOnlyTriggers` → DB มี 4 triggers (TR_AuditLogs/ConsentRecords/CreditTransactions/NotifDelivery_NoModify)
> และ UPDATE/DELETE บนตาราง append-only โดน THROW 51001–51004

**สำหรับ DB ที่ deploy ด้วยทางเลือก A มาก่อน (Option A → forward migrate):** ถ้า DB ถูกสร้างจาก
`schema.sql` รุ่นเก่าแล้วค่อย `dotnet ef database update` อาจชน seed ที่ schema.sql ใส่ไว้แล้ว
(เช่น `ConfigVersions` Id ซ้ำ) — ให้ apply schema effect ของ migration ที่ค้างด้วยมือแบบ idempotent
(เช่น `ALTER TABLE ... ADD <col>` ถ้ายังไม่มี) แล้ว stamp migration นั้นใน `__EFMigrationsHistory`
จากนั้น `database update` จะลงเฉพาะ `AddAppendOnlyTriggers` (CREATE OR ALTER ปลอดภัย)

checklist diff (เผื่อเพิ่ม table/feature ใหม่ในอนาคต) — ของพวกนี้ EF generate ครบแล้ว ยกเว้น trigger:

- [ ] filtered unique index: `UX_Users_NormalizedEmail`, `UX_Membership_LivePerUser`,
      `UX_CreditTx_Idempotency`, `UX_CreditTx_TypeRef`, `UX_Notif_NoDup`
- [ ] `CHECK` constraints, `NEWSEQUENTIALID()` defaults, `ROWVERSION`
- [ ] **append-only triggers** — EF **ไม่ generate trigger ให้** ต้องเพิ่มผ่าน `migrationBuilder.Sql(...)`
      โดยคัดลอกจาก `sql/triggers.sql` (ใช้ `CREATE OR ALTER` เพื่อให้ idempotent) — ดูตัวอย่างใน
      migration `AddAppendOnlyTriggers` ที่มีอยู่แล้ว

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
- **DB least-privilege** (defence-in-depth เสริม trigger): `DENY UPDATE, DELETE ON dbo.AuditLogs / dbo.ConsentRecords / dbo.CreditTransactions / dbo.NotificationDeliveryLog TO [app_role];`
  (4 ตารางนี้มี append-only trigger THROW 51001–51004 อยู่แล้ว; DENY คือชั้นเสริมระดับ permission)
- **Trigger deploy**: ทั้ง `deploy.ps1` และ `dotnet ef database update` ติดตั้ง trigger ครบ (CREATE OR ALTER, idempotent)
  — หลัง deploy ทุกครั้งให้ verify `SELECT COUNT(*) FROM sys.triggers WHERE name LIKE 'TR\_%\_NoModify' ESCAPE '\'` ต้อง = 4
- **Backup/rollback**: backup DB ก่อนทุก migration; forward = `dotnet ef database update`; rollback = `dotnet ef database update <PreviousMigrationId>`
  (เช่น ย้อน trigger: `... update 20260624063001_AddFieldGapsM1M2M3` → migration `AddAppendOnlyTriggers.Down()` จะ DROP trigger ทั้ง 4); เก็บ `dotnet ef migrations script <from> <to>` เป็น artifact คู่กับ backup

---

## 8. Troubleshooting

| อาการ | สาเหตุ/วิธีแก้ |
|---|---|
| `No connection string...` ตอน startup | ไม่มีทั้ง `MARKETPLACE_CONNECTION` และ `ConnectionStrings:MarketplaceDb` — ตั้งอย่างใดอย่างหนึ่ง |
| Api throw `Jwt:SigningKey is missing...` | รันนอก Development โดยไม่มี key — `dotnet user-secrets set "Jwt:SigningKey" ...` หรือ env `Jwt__SigningKey` |
| `dotnet ef` not found | `dotnet tool install --global dotnet-ef --version 8.*` แล้วเพิ่ม `~/.dotnet/tools` ใน PATH |
| migration ไม่มี trigger/filtered index | filtered index มาจาก EF แล้ว; trigger มาจาก migration `AddAppendOnlyTriggers` (หรือ `sql/triggers.sql` ในทางเลือก A) — verify ว่ามี 4 triggers หลัง deploy |
| `database update` ล้ม `Violation of PRIMARY KEY ... ConfigVersions` | DB เดิมสร้างจาก schema.sql (ทางเลือก A) ที่ seed ไว้แล้ว ชนกับ `InsertData` ของ migration — apply schema effect (`ALTER TABLE ... ADD`) ของ migration ที่ค้างด้วยมือแบบ idempotent แล้ว stamp migration นั้น (ดู §3 ทางเลือก B) |
| `sqlcmd` SSL cert error | เพิ่ม `-C` (trust server cert); และ `-I` เสมอ (QUOTED_IDENTIFIER ON) ไม่งั้น filtered index ล้ม |
| Api/Worker boot ไม่ขึ้นบน prod (KYC) | `Kyc:Mode=Mock` ใน Production ถูก hard-guard ปฏิเสธ (LEGAL #2) — ตั้ง `Kyc__Mode=Ndid` + NDID secrets |
| NU1605 package downgrade | align ทุก `Microsoft.EntityFrameworkCore.*` / `Microsoft.Extensions.*` เป็น 8.0.x |
| LocalDB ต่อไม่ได้ | `sqllocaldb start MSSQLLocalDB` หรือชี้ `MARKETPLACE_CONNECTION` ไป SQL Server อื่น |
