# QA Consistency Report — SRS_MVP (v0.2) ↔ DB Schema

| Field | Value |
|---|---|
| Document | QA Consistency Report — SRS ↔ DB Schema |
| Reviewer | qa (QA Engineer) |
| Date | 2026-06-17 |
| Inputs | `docs/SRS_MVP.md` (FR-01..31, v0.2), `docs/DB_Schema.md`, `sql/schema.sql` (32 ตาราง) |
| Scope | Traceability FR→ตาราง, ความขัดแย้งเชิงตรรกะ, ความครบ business rule, PDPA/Audit, gap |

---

## 0. สรุปผล (Executive Summary)

**ผลรวม: ⚠️ ผ่านแบบมีข้อสังเกต — ต้องแก้ก่อน dev (Conditional Pass)**

Schema มีคุณภาพสูงและสอดคล้องกับเจตนากฎหมาย (no-touch, single-level referral, non-cashable credit, PDPA soft-delete, append-only audit) ได้ดีมาก โครงสร้างรองรับ FR ส่วนใหญ่ครบ FR-01..FR-31 มีตารางรองรับเกือบทั้งหมด

**แต่** มี **gap เชิงโครงสร้าง** ที่กระทบ FR สำคัญและจะกลายเป็นหนี้ทางเทคนิค/ความเสี่ยงกฎหมาย ถ้าเริ่มโค้ดเลย โดยเฉพาะ:
- **ไม่มีตาราง Notification/แจ้งเตือน** ทั้งที่ FR-27 บังคับแจ้งเตือน 14/3/1 วัน + ต้อง log การแจ้ง (เป็นเงื่อนไขกฎหมายคุ้มครองผู้บริโภค Legal #8) → **Blocker**
- **CreditTransactions ไม่มี idempotency / unique key กัน double-credit** กระทบ FR-28/FR-30 (หักเครดิตซ้ำ/ให้รางวัลซ้ำ) → **Blocker เชิง integrity**
- **ไม่มีตาราง/คอลัมน์เก็บ "ความเห็นผู้ประเมินอิสระ" (FR-07)** ทั้งที่เป็น in-scope IS-8 และมีความเสี่ยงกฎหมาย (Legal #2)
- บาง state machine ไม่ครบใน CHECK (Auction ขาด `Ended/Settled` ที่ชัดเจน, Trust state `PendingBanReview` ไม่มีที่เก็บ)

**คะแนนความพร้อมก่อน dev: 72/100**
(โครงหลักแน่น แต่ขาด 3 ตารางสำคัญ + idempotency ที่ต้องตัดสินใจก่อนเขียน entity/migration)

| ระดับ | จำนวน |
|---|---|
| 🔴 Blocker | 6 |
| 🟡 ควรแก้ | 9 |
| 🔵 ข้อเสนอแนะ | 7 |

---

## 1. Traceability Matrix — FR ↔ ตาราง/คอลัมน์

| FR | สรุป | ตาราง/คอลัมน์ที่รองรับ | สถานะ |
|---|---|---|---|
| FR-01 | สมัคร Normal + consent + Trust=100 | `Users`, `ConsentRecords`, `TrustScores`(DEFAULT 100), `Memberships`(Trial), `AuditLogs` | ✅ ครบ |
| FR-02 | e-KYC NDID (เก็บผลเท่านั้น) | `KycVerifications`(Provider/ProviderReference/Level), `KycStatuses` | ✅ ครบ |
| FR-03 | Fallback KYC encrypt + retention + ลบ | `KycSensitiveData`(FullNameMasked/NationalIdHash/RetentionExpiresAtUtc) | ✅ ครบ (รอ Always Encrypted ตอน deploy) |
| FR-04 | จัดการ consent + สิทธิ์เจ้าของข้อมูล (export/ลบ) | `ConsentRecords`, `Users.IsDeleted/IsAnonymized/DeletedAtUtc` | 🟡 บางส่วน — **ไม่มีตาราง DSAR/คำขอ export-ลบ + SLA** (FR-04 ระบุ "สร้างคำขอ, แจ้ง SLA") |
| FR-05 | อัพเกรด Premium / tier + วันหมดอายุ + pro-rate | `Memberships`(Tier/PaidThroughUtc), `FeeInvoices`(MembershipUpgrade), `MembershipTiers` | 🟡 บางส่วน — ไม่มีคอลัมน์เก็บ logic pro-rate / proration record |
| FR-06 | สร้างประกาศ (ต้อง membership active) + disclaimer | `Products`, `ProductImages`, `Memberships`(gate ระดับ app) | 🟡 — disclaimer/มูลค่าเกินเกณฑ์บังคับ KYC ไม่มี config field |
| FR-07 | ความเห็นผู้ประเมินอิสระ + disclaimer | **ไม่มีตารางรองรับ** | 🔴 **GAP** |
| FR-08 | ค้นหา/ดู Active (Guest) | `Products.Status='Active'`, index `IX_Products_Category_Status` | ✅ ครบ |
| FR-09 | ตั้งประมูล | `Auctions`(StartingPrice/Reserve/Increment/Start/End/Status) | ✅ ครบ |
| FR-10 | Bid (ต้อง membership active) | `Bids`, gate `Memberships` (ระดับ app) | ✅ ครบ (มี RowVersion → NFR-PF3) |
| FR-11 | Anti-shill | `Bids`(IpAddressHash/DeviceFingerprintHash/IsFlaggedShill/RelationshipFlag), index `IX_Bids_Shill` | 🟡 — ไม่มีที่เก็บ "หลักฐาน/เครือข่ายบัญชี" แบบ structured (FR ระบุ log หลักฐาน) |
| FR-12 | ปิดประมูล (worker) | `Auctions.Status/WinningBidId`, index `IX_Auctions_Status_End`, `Bids.Status='Won'` | 🟡 — ดู Finding R-03 (state machine ขาด step) |
| FR-13 | เริ่ม Trust=100 + history แถวแรก | `TrustScores`(DEFAULT 100), `TrustScoreHistory` | ✅ ครบ |
| FR-14 | ปรับ Trust จากเหตุการณ์ | `TrustScoreHistory`(Delta/ScoreAfter/ReasonCodeId/RelatedTransactionId) | ✅ ครบ |
| FR-15 | ลงทัณฑ์อัตโนมัติ + due process + อุทธรณ์ | `PenaltyActions`, `BlacklistEntries`(ReviewStatus/AppealStatus), `Users.AccountStatus` | 🟡 — **ไม่มี state `PendingBanReview`** ใน `AccountStatus` CHECK |
| FR-16 | ดูประวัติ Trust ตนเอง | `TrustScores`, `TrustScoreHistory`, index `IX_TSHist_User` | ✅ ครบ |
| FR-17 | Warn-on-deal (Blacklist ภายใน) | `BlacklistEntries`(ReasonCodeId/InternalNote/IsActive), index `IX_Blacklist_User` | ✅ ครบ (ถ้อยคำกลางผ่าน ReasonCode) |
| FR-18 | No-touch payment | `Transactions`(AgreedAmount/ExternalPaymentNote, ไม่มี wallet/held) | ✅ ครบ (ออกแบบดีมาก) |
| FR-19 | ยืนยันรับของ + รีวิว | `Transactions.ConfirmedAtUtc`, `Reviews`(UNIQUE per Tx) | ✅ ครบ |
| FR-20 | เปิด dispute + freeze trust | `Disputes`(Status/ReasonCodeId) | 🟡 — ไม่มี flag "freeze trust adjustment" บน Tx/Dispute |
| FR-21 | ตัดสิน dispute (ไม่จ่ายเงินแทน) | `Disputes.Resolution/HandledByUserId` | ✅ ครบ |
| FR-22 | เก็บค่าธรรมเนียมแยกขาด | `FeeInvoices`(FeeType, แยกจาก Transactions) | ✅ ครบ |
| FR-23 | ค่าลงประกาศ/โปรโมต + ใบเสร็จ | `FeeInvoices`(Listing/Premium/Featured), `ExternalPaymentRef` | ✅ ครบ |
| FR-24 | Audit log append-only | `AuditLogs`(Actor/Action/Entity/Before/After/Ip) | 🟡 — append-only เป็น "ความตั้งใจ" ไม่มีกลไกบังคับ (ดู A-01) |
| FR-25 | Admin due process log | `AuditLogs`, `BlacklistEntries.ReviewedByUserId`, `Disputes.HandledByUserId` | ✅ ครบ |
| FR-26 | รายงาน AML-friendly | `AuditLogs` + index ตาม entity/actor | 🔵 — query ได้ แต่ไม่มี view/report object เฉพาะ |
| FR-27 | Membership lifecycle + trial + แจ้งเตือน 14/3/1 + auto-renew | `Memberships`(Status/Trial/PaidThrough/AutoRenew), `MembershipTiers.AnnualPriceTHB`, `FeeInvoices` | 🔴 — **ไม่มีตาราง Notification log** (FR บังคับ "log การแจ้ง") + ไม่มี "วิธีชำระที่ยินยอม (payment method)" สำหรับ auto-renew |
| FR-28 | Referral single-level + credit + anti-abuse | `ReferralCodes`(UNIQUE), `Referrals`(UNIQUE referred, no upline), `CreditTransactions` | 🟡 — single-level/non-cashable บังคับด้วยโครงสร้างได้ดี แต่ **ไม่มี idempotency กัน double-reward + ไม่มี field anti-abuse (device/IP ตอนสมัคร)** |
| FR-29 | Credit ledger append-only โปร่งใส | `CreditAccounts`, `CreditTransactions`(Type/BalanceAfter/ExpiresAt) | 🟡 — append-only ไม่บังคับด้วย DB; ดู A-01 |
| FR-30 | ใช้เครดิตซื้อโปรโมต | `ListingPromotions`(CreditTransactionId→PromoSpend), `PromotionPackages` | 🟡 — ไม่มี idempotency/unique กันหักเครดิตซ้ำตอน retry |
| FR-31 | Admin config ราคา/เครดิต/แพ็กเกจ + เก็บประวัติเวอร์ชัน + effective date | `MembershipTiers`, `PromotionPackages` (เป็น lookup เฉย ๆ) | 🔴 — **ไม่มีตาราง config/history + effective date + เวอร์ชัน** (FR-31 บังคับ "เก็บประวัติเวอร์ชัน config + effective date + ไม่ย้อนหลัง") |

### Orphan ตาราง (ตารางที่ไม่มี FR อ้างถึงตรง ๆ)
- ไม่พบ orphan ที่แท้จริง — ทุกตารางสนับสนุน FR/NFR อย่างน้อยหนึ่งข้อ
- `TransactionStatusHistory` — สนับสนุน FR-24/NFR-A1 (audit ระดับ tx) ถือว่า justified
- `UserProfiles` — ไม่มี FR ระบุ field display โดยตรง แต่ implied โดย FR-01/listing → 🔵 ปกติ

### FR ที่ไม่มีตารางรองรับ (สรุป)
- **FR-07** (ความเห็นผู้ประเมินอิสระ) — ไม่มีตารางเลย
- **FR-27** (notification log การแจ้งเตือน) — ไม่มีตาราง
- **FR-31** (config versioning + effective date) — ไม่มีตาราง
- **FR-04** (DSAR request/SLA) — ไม่มีตาราง (มีแค่ flag soft-delete)

---

## 2. Findings

### 🔴 Blocker (ต้องแก้ก่อนเริ่ม dev)

**B-01 — ไม่มีตาราง Notification / log การแจ้งเตือน (FR-27)**
- ปัญหา: FR-27 บังคับ "ส่งการแจ้งเตือนล่วงหน้า 14/3/1 วัน + **log การแจ้ง**" และ auto-renew ทำได้ "เฉพาะเมื่อได้แจ้งเตือนล่วงหน้าแล้ว" แต่ schema ไม่มีตารางเก็บว่าแจ้งใคร/เมื่อไหร่/ช่องทางไหน
- ผลกระทบ: **เสี่ยงกฎหมาย** — พิสูจน์ไม่ได้ว่าแจ้งก่อนตัดเงิน (Legal #8, พ.ร.บ.คุ้มครองผู้บริโภค); auto-renew ทำไม่ได้ตาม precondition; ทำ idempotency ของ worker แจ้งเตือนไม่ได้ (แจ้งซ้ำ/ไม่แจ้ง)
- ข้อเสนอแก้: เพิ่มตาราง `Notifications` (UserId, Type เช่น TrialExpiring/RenewalDue, Channel email/in-app, RelatedMembershipId, ScheduledFor, SentAtUtc, Status) + unique กันส่งซ้ำ (UserId, Type, MembershipId, milestone 14/3/1)

**B-02 — CreditTransactions ไม่มี idempotency กัน double-credit (FR-28/FR-29/FR-30)**
- ปัญหา: ledger ใช้ `RefId NVARCHAR(64)` แบบ loose ไม่มี unique constraint บน (Type, RefId) → retry/ดับเบิลคลิก/worker รันซ้ำ จะ "ให้ referral reward ซ้ำ" หรือ "หักเครดิตโปรโมตซ้ำ" ได้
- ผลกระทบ: เครดิตคลาดเคลื่อน, balance เพี้ยน, ตรวจสอบ AML/ร้องเรียนยาก, ขัดเจตนา "ledger โปร่งใส" (FR-29)
- ข้อเสนอแก้: เพิ่ม `UNIQUE (Type, RefId)` (filtered RefId IS NOT NULL) หรือคอลัมน์ `IdempotencyKey` UNIQUE; ฝั่ง app หักเครดิตใน transaction เดียวกับการ insert ledger + อัปเดต balance

**B-03 — ไม่มีตารางเก็บ "ความเห็นผู้ประเมินอิสระ" (FR-07 / IS-8)**
- ปัญหา: FR-07 เป็น in-scope (IS-8) มี acceptance ชัด (แสดงผู้ประเมิน + disclaimer + ห้ามคำว่า "รับประกัน/ของแท้ 100%") แต่ไม่มีตาราง/คอลัมน์เก็บความเห็นนี้เลย
- ผลกระทบ: ทำ FR-07 ไม่ได้; ความเสี่ยงกฎหมาย Legal #2 (ความรับผิดจากการรับประกันความแท้) จัดการไม่ได้ในระดับ data
- ข้อเสนอแก้: เพิ่มตาราง `AppraisalOpinions` (ProductId, AppraiserId/AppraiserName, OpinionText, DisclaimerVersion, CreatedAtUtc) + (option) ตาราง/role ผู้ประเมินอิสระ

**B-04 — ไม่มีตาราง Config versioning + effective date (FR-31)**
- ปัญหา: FR-31 บังคับ "เก็บประวัติเวอร์ชัน config + effective date + การเปลี่ยนราคาไม่ย้อนหลัง" แต่ `MembershipTiers`/`PromotionPackages` เป็น lookup ที่ถูก overwrite ได้ ไม่มีประวัติ/effective date
- ผลกระทบ: เปลี่ยนราคาแล้วกระทบรอบที่ชำระไปแล้ว (ผิด FR-31 + เสี่ยงผู้บริโภค); ตรวจย้อนหลังไม่ได้ว่าใครชำระราคาเท่าไรตอนไหน
- ข้อเสนอแก้: เพิ่มตาราง `ConfigVersions`/`PricingHistory` (ConfigKey, Value, EffectiveFromUtc, CreatedByUserId) หรือ snapshot ราคาที่ FeeInvoice/Membership ตอนออกบิล (เก็บ `PricedAmount` ใน Memberships/FeeInvoices)

**B-05 — AccountStatus ขาด state `PendingBanReview` (FR-15 / §6.2 State Machine)**
- ปัญหา: State machine ใน SRS §6.2 มี state `PendingBanReview` (เสนอ ban รอ Admin ยืนยัน — human-in-the-loop, DP-3) แต่ `Users.AccountStatus` CHECK มีแค่ `Active/Suspended/Banned` ไม่มี state รอพิจารณา
- ผลกระทบ: บังคับ "ห้าม auto-ban ถาวรล้วน" (DP-3) ในระดับ data ไม่ได้; ระบบแยกไม่ออกระหว่าง "เสนอ ban" กับ "ban แล้ว"
- ข้อเสนอแก้: เพิ่ม `'PendingBanReview'` ใน CHECK ของ `Users.AccountStatus` (และ map state machine ให้ครบ)

**B-06 — Append-only ของ AuditLogs / ConsentRecords / CreditTransactions ไม่ถูกบังคับด้วย DB (FR-24/FR-29, NFR-A1)**
- ปัญหา: เอกสารระบุ append-only/immutable แต่ schema ไม่มีกลไกบังคับ (ไม่มี trigger ห้าม UPDATE/DELETE, ไม่มี temporal table, ไม่มี deny permission) — เป็นแค่ข้อตกลงระดับ app
- ผลกระทบ: FR-24 ระบุชัด "พยายามแก้/ลบ log → ระบบปฏิเสธ (immutable)"; ถ้าบังคับแค่ระดับ app จะไม่ผ่านเจตนา AML/due process เมื่อมีการเข้าถึง DB ตรง
- ข้อเสนอแก้: เพิ่ม INSTEAD OF UPDATE/DELETE trigger ที่ปฏิเสธ หรือใช้ DENY UPDATE/DELETE บน role ของ app + (ถ้าต้องการ) temporal/ledger table ของ SQL Server 2022

### 🟡 ควรแก้

**Y-01 — FR-04 ไม่มีตาราง DSAR (คำขอ export/ลบข้อมูล + SLA)**
- มีแค่ flag soft-delete บน Users; FR-04 ระบุ "สร้างคำขอ, แจ้ง SLA, log" → เพิ่มตาราง `DataSubjectRequests` (Type Export/Delete/Access, Status, SlaDueAtUtc, RequestedAtUtc, HandledByUserId)

**Y-02 — FR-28 anti-abuse referral ไม่มี field/โครงสร้างรองรับการตรวจจับ**
- FR-28 ต้องตรวจ self-referral, บัญชีซ้ำ device/IP/KYC; โครงสร้างกัน self-referral (CHECK) + referred-once (UNIQUE) ได้ แต่ไม่มีที่เก็บ device/IP hash ตอนสมัคร เพื่อจับ "บัญชีปลอม/ซ้ำ" → เพิ่ม signup `RegistrationIpHash/DeviceFingerprintHash` หรือผูกกับ AuditLogs ที่ structured

**Y-03 — Auto-renew ไม่มี "วิธีชำระที่ยินยอม" (FR-27)**
- FR-27 auto-renew ทำได้เฉพาะ "มีวิธีชำระที่ยินยอมไว้" แต่ schema ไม่มีที่เก็บ payment method/mandate consent → เพิ่ม `PaymentMandates` หรือ field บน Memberships (ConsentedPaymentRef) — ระวังไม่ให้ขัด no-touch (นี่คือเงินบริษัท ไม่ใช่ flow ซื้อขาย จึงทำได้)

**Y-04 — FR-15 freeze trust ระหว่าง dispute ไม่มี flag (FR-20)**
- FR-20 ระบุ "freeze การปรับ Trust Score อัตโนมัติของกรณีนั้นจนกว่าจะตัดสิน" แต่ไม่มี flag บน Transaction/Dispute ให้ worker เช็ค → เพิ่ม `Disputes.FreezesTrustAdjustment BIT` หรือ derive จาก `Transactions.Status='Disputed'`

**Y-05 — Membership trial ไม่มี CHECK ผูก trial เข้ากับ Status='Trial'**
- TrialStartsAt/EndsAt เป็น nullable อิสระ; ไม่มี constraint ว่า Status='Trial' ต้องมี TrialEndsAtUtc → เสี่ยง trial ไม่มีวันหมด (ขัด FR-27/Legal #8) → เพิ่ม CHECK เชิงเงื่อนไข

**Y-06 — Membership ไม่มีคอลัมน์เก็บราคาที่ชำระจริง (สนับสนุน FR-31 "ไม่ย้อนหลัง")**
- ควร snapshot `PaidAmountTHB` ที่ Membership/FeeInvoice ตอนชำระ เพื่อกันราคาที่เปลี่ยนภายหลังมากระทบรอบเดิม

**Y-07 — Products ไม่มี config "มูลค่าเกินเกณฑ์บังคับ KYC" (FR-06)**
- FR-06 "มูลค่าเกินเกณฑ์ → บังคับ Normal ทำ KYC" ต้องมี threshold ที่ตั้งค่าได้ (ผูกกับ B-04 config table)

**Y-08 — ListingPromotions ↔ CreditTransactions ความสัมพันธ์ nullable หลวม (FR-30)**
- `CreditTransactionId` nullable + ไม่มี unique → promotion หนึ่งอาจไม่ผูก ledger หรือผูกซ้ำ; ควร NOT NULL + UNIQUE หลังหักเครดิตสำเร็จ (สอดคล้อง B-02)

**Y-09 — Bids ไม่มี constraint/ที่เก็บ "bid > current + step" และ anti-shill evidence**
- กติกา bid step บังคับระดับ app เท่านั้น (ยอมรับได้) แต่ FR-11 "log หลักฐาน" ควรมีที่เก็บ structured (ผูก AuditLogs EntityType='Bid' หรือ ShillEvidence table)

### 🔵 ข้อเสนอแนะ

- **S-01** เพิ่ม view/stored proc สำหรับ FR-26 (AML report) เพื่อมาตรฐาน query
- **S-02** `Disputes.ReasonCodeId` เป็น NULL ได้ — พิจารณาบังคับ NOT NULL เพื่อสอดคล้องการใช้ reason code มาตรฐาน
- **S-03** เพิ่ม index `IX_Membership_Expiry` บน (Status, PaidThroughUtc/TrialEndsAtUtc) ให้ worker สแกนหมดอายุ/แจ้งเตือนได้เร็ว (สนับสนุน FR-27 worker)
- **S-04** `CreditTransactions.Type` ไม่มี `Revoke` ทั้งที่ FR-28/FR-29 พูดถึง "ริบเครดิต (revoke)" — ปัจจุบันต้องใช้ `Adjustment` (กำกวม) → พิจารณาเพิ่ม Type `Revoke`
- **S-05** เพิ่ม FK `ListingPromotions.ProductId` ON DELETE behavior + พิจารณา index ครอบ active per product
- **S-06** AuditLogs ควรมี `CorrelationId` (NFR-A2 ระบุ "correlation id ต่อธุรกรรม") — ปัจจุบันไม่มีคอลัมน์นี้
- **S-07** เพิ่มการเก็บ "การเข้าถึง log เอง" (NFR-A3) — ปัจจุบันไม่มี audit-on-audit

---

## 3. ตรวจเฉพาะจุด (ตามโจทย์)

### 3.1 State Machine ครบใน schema ไหม
| State machine | สถานะใน SRS | บังคับใน schema (CHECK) | ผล |
|---|---|---|---|
| Membership | Trial/Active/Expired/Cancelled | `CK_Membership_Status` ครบ | ✅ |
| Auction | Scheduled/Open/Closed/Cancelled | `CK_Auctions_Status` ครบ | ✅ |
| Bid | Active/Outbid/Won/Retracted/Voided | `CK_Bids_Status` ครบ | ✅ |
| Transaction | Pending/Transferred/Confirmed/Disputed/Cancelled (ไม่มี Held/Escrow) | `CK_Tx_Status` ครบ + no wallet | ✅ (no-touch ถูกต้อง) |
| Dispute | Open/UnderReview/Resolved/Rejected/Escalated | `CK_Dispute_Status` ครบ | ✅ |
| Blacklist | ReviewStatus + AppealStatus | CHECK ครบ | ✅ |
| **Trust/Account** | Active/Suspended/**PendingBanReview**/Banned | `CK_Users_Status` **ขาด PendingBanReview** | 🔴 B-05 |

### 3.2 No-touch — มีเงิน/wallet หลุดมาไหม
- ✅ **ผ่าน** — `Transactions` ไม่มี balance/held/wallet; เงินบริษัทแยกใน `FeeInvoices`; เครดิตเป็น point ใน `CreditAccounts` (non-cashable, มี ExpiresAt, ไม่มี Withdraw/Transfer type) สอดคล้อง FR-18/FR-22/FR-28 และ Legal #1/#10 อย่างดี

### 3.3 Referral single-level + credit non-cashable บังคับด้วยโครงสร้างจริงไหม
- ✅ **single-level** — `Referrals` ไม่มี ParentReferralId/UplineUserId/Level + UNIQUE(ReferredUserId) + CHECK not-self → flat จริงตามดีไซน์
- ✅ **non-cashable/non-transferable** — `CreditTransactions.Type` ไม่มี Withdraw/CashOut/Transfer; balance CHECK ≥0; expirable
- 🟡 **แต่** ขาด idempotency (B-02) และ Type `Revoke` (S-04)

### 3.4 PDPA / Audit ครบไหม
- ✅ ConsentRecords (versioned, append-only intent), AuditLogs (before/after, ip hash), soft-delete/anonymize บน Users (รองรับ FR-04 ระดับ data)
- 🟡 ขาด: ตาราง DSAR (Y-01), การบังคับ append-only ระดับ DB (B-06), CorrelationId (S-06), audit-on-audit (S-07)
- 🟡 action สำคัญที่ต้องลง AuditLogs (membership/credit/referral/promotion) — เอกสาร schema ระบุเป็น "ต้องเขียนระดับ app" ไม่มีบังคับ; ยอมรับได้แต่ควรมี test ครอบ

---

## 4. Gap ที่แนะนำเพิ่ม (สรุปรวม)

| # | สิ่งที่ควรเพิ่ม | รองรับ FR | ระดับ |
|---|---|---|---|
| G-1 | ตาราง `Notifications` (+log การแจ้ง 14/3/1, unique กันซ้ำ) | FR-27 | 🔴 |
| G-2 | Idempotency บน `CreditTransactions` (UNIQUE Type+RefId / IdempotencyKey) | FR-28/29/30 | 🔴 |
| G-3 | ตาราง `AppraisalOpinions` (ความเห็นผู้ประเมินอิสระ) | FR-07 | 🔴 |
| G-4 | ตาราง `ConfigVersions/PricingHistory` (effective date + ไม่ย้อนหลัง) | FR-31 | 🔴 |
| G-5 | เพิ่ม `PendingBanReview` ใน Users.AccountStatus | FR-15/DP-3 | 🔴 |
| G-6 | บังคับ append-only ระดับ DB (trigger/deny/temporal) | FR-24/29, NFR-A1 | 🔴 |
| G-7 | ตาราง `DataSubjectRequests` (DSAR + SLA) | FR-04 | 🟡 |
| G-8 | `PaymentMandates` / payment-consent สำหรับ auto-renew | FR-27 | 🟡 |
| G-9 | signup device/IP hash + anti-abuse field referral | FR-28 | 🟡 |
| G-10 | freeze-trust flag ระหว่าง dispute | FR-20 | 🟡 |
| G-11 | KYC threshold config สำหรับมูลค่าสินค้า | FR-06 | 🟡 |
| G-12 | `CorrelationId` บน AuditLogs | NFR-A2 | 🔵 |
| G-13 | Credit Type `Revoke` | FR-28/29 | 🔵 |

---

## 5. Concurrency (NFR-PF3) — ผ่านไหม
- ✅ มี `ROWVERSION` บน `Auctions`, `Bids`(ไม่มี — ใช้ผ่าน Auction), `Products`, `Memberships`, `Transactions`, `TrustScores`, `CreditAccounts`
- หมายเหตุ: `Bids` เองไม่มี RowVersion แต่ concurrent bid คุมที่ `Auctions.CurrentHighBid` (มี RowVersion) — ถือว่ารองรับ optimistic concurrency ตาม NFR-PF3 ✅ (แต่ควรระบุ pattern ให้ชัดในเอกสาร dev)

---

## 6. สรุปสำหรับผู้บริหาร/ทีม
Schema นี้ "พร้อมเป็นฐานเริ่ม dev ได้" หลังปิด 6 Blocker โดยเฉพาะ **ตาราง Notification (G-1), Config versioning (G-4), Appraisal (G-3)** ซึ่งเป็น FR ที่ "ไม่มีตารางรองรับเลย" และมีนัยกฎหมาย และ **idempotency เครดิต (G-2)** + **append-only enforcement (G-6)** ซึ่งเป็นความเสี่ยง integrity/AML ที่แก้ทีหลังยาก. เจตนากฎหมายหลัก (no-touch, single-level, non-cashable, PDPA) ถูกสะท้อนในโครงสร้างได้ดีมาก ไม่พบเงิน/wallet หลุด.

> หมายเหตุ: รายงานนี้ตรวจความสอดคล้องเชิงเอกสาร/โครงสร้าง ไม่แทนการ sign-off ของทนายมีใบอนุญาตตาม SRS §8.
