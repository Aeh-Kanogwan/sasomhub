# Software Requirement Specification (SRS) — MVP
## Marketplace ขายของสะสม (Collectibles Marketplace)

| Field | Value |
|---|---|
| Document | SRS — MVP Release |
| Version | 0.3 (Draft for Legal Review) |
| Author | sa-analyst (System Analyst) |
| Date | 2026-06-17 |
| Stack | .NET 8 Web API + EF Core + SQL Server + Worker/BackgroundService |
| Status | DRAFT — ต้องผ่านการตรวจของทนายมีใบอนุญาตก่อนเริ่ม dev |

> **Legal-driven document.** เอกสารนี้ปรับ Core เดิมจาก Blueprint ให้สอดคล้องกับข้อสรุปทีม Legal (2026-06-17): no-touch payment, ห้ามอ้างการันตีความแท้, Blacklist เป็นระบบเตือนภายในมี due process, e-KYC/NDID, ตัด Pre-Order/escrow ออก, เพิ่ม anti-shill bidding + audit log + due process.
>
> **v0.2 (2026-06-17) — เพิ่มตามคำสั่งเจ้าของระบบ:** Membership แบบ active-required สำหรับ bid/listing, ค่าสมาชิกรายปี 3 ระดับ + auto-renew + แจ้งเตือนก่อนหมดอายุ, Free Trial 3 เดือนนับจากวันสมัคร (เปิดเผยราคา+วันหมดตั้งแต่สมัคร — คุ้มครองผู้บริโภค), ระบบ Referral + Credit แบบ **single-level เท่านั้น เครดิตถอน/แลกเงิน/โอนไม่ได้** (เลี่ยง พ.ร.บ.ขายตรงฯ และ พ.ร.บ.ระบบการชำระเงินฯ), ใช้เครดิตซื้อตำแหน่งโปรโมต listing. หลักการ **no-touch / รายได้แพลตฟอร์มแยกขาดจาก flow ซื้อขาย** ยังคงเดิมทุกประการ. เพิ่ม FR-27..FR-31 (FR-01..FR-26 เดิมคงไว้).
>
> **v0.3 (2026-06-17) — ปิด 6 Blocker จาก QA Consistency Report (B-01..B-06):** เพิ่ม FR-32..FR-37 และ NFR ใหม่ เพื่อปิดช่องว่างเชิงโครงสร้างที่ทีม QA พบ ได้แก่ (1) **FR-32 Notification log** แจ้งเตือน + บันทึกหลักฐานการแจ้ง 14/3/1 วันแบบกันส่งซ้ำ เป็น precondition ของ auto-renew (B-01); (2) **FR-33 Credit Idempotency & Atomicity** การให้รางวัล referral/หักเครดิตต้อง idempotent + atomic (B-02); (3) **FR-34 ข้อมูลความเห็นผู้ประเมินอิสระ** ในระดับ data + คงข้อห้ามคำว่ารับประกัน/การันตี/ของแท้ 100% (B-03); (4) **FR-35 Config Versioning + Price Snapshot** เก็บประวัติเวอร์ชัน/effective date + snapshot ราคาที่ชำระจริง (B-04); (5) **FR-36 PendingBanReview state** ใน account/trust state machine §6.2 (B-05); (6) **FR-37 + NFR-A4 Append-only ระดับ DB** บังคับ immutable ที่ฐานข้อมูลสำหรับ AuditLogs/ConsentRecords/CreditTransactions (B-06). ปรับ FR-27 ให้อ้าง FR-32, ผูก FR-06 KYC-threshold และ auto-renew payment mandate เข้ากับ config (FR-35). คงหลัก no-touch ทุกประการ — notification/credit/config/mandate เป็นเรื่องบริการ/เงินบริษัท ไม่ใช่ flow เงินซื้อขาย. FR-01..FR-31 เดิมคงไว้ครบ.

---

## 1. ขอบเขต MVP (Scope)

### 1.1 In-Scope
| # | รายการ | เหตุผล/อ้างอิงกฎหมาย |
|---|---|---|
| IS-1 | สมัครสมาชิก 3 ระดับ: Normal / Verified / Premium | Core เดิม |
| IS-2 | e-KYC ผ่าน NDID (รับเฉพาะผล verified) สำหรับ Verified/Premium | Legal #4 — ลดความเสี่ยง PDPA จากการเก็บภาพบัตร |
| IS-3 | ลงประกาศขายสินค้า (Listing) + รูป + รายละเอียด | Core เดิม |
| IS-4 | ระบบประมูล/Bidding พร้อม anti-shill detection | Legal #6 — กันปั่นราคา |
| IS-5 | Trust Score + ระบบลงทัณฑ์อัตโนมัติ + **due process/อุทธรณ์** | Legal #6 — เพิ่ม due process |
| IS-6 | Blacklist ภายใน = "ระบบเตือนคู่ธุรกรรม" (warn-on-deal) | Legal #3 — ไม่ประจาน, ถ้อยคำกลาง, อุทธรณ์ได้ |
| IS-7 | **โอนตรงเคร่งครัด (no-touch)** + ยืนยันรับของ + รีวิว | Legal #1 — เงินไม่ผ่านระบบ/บัญชีบริษัทเลย |
| IS-8 | "ความเห็นประกอบจากผู้ประเมินอิสระ" + disclaimer | Legal #2 — ห้ามอ้างรับประกัน/การันตีความแท้ |
| IS-9 | Dispute / รายงานปัญหา | คุ้มครองผู้ใช้โดยไม่เป็นผู้รับประกัน |
| IS-10 | ค่าธรรมเนียม: ค่าสมาชิก / ค่าลงประกาศ / ค่า Premium | Legal #1 — รายได้แยกขาดจาก flow ซื้อขาย |
| IS-11 | Audit log ทุกธุรกรรม + consent/PDPA + log AML-friendly | Legal #6, #7 |
| IS-12 | **Membership active-required**: bid/ซื้อ และลงขาย ต้องเป็นสมาชิกที่ membership ยัง active เท่านั้น (Guest ดู/ค้นหาได้อย่างเดียว) | Core ใหม่ v0.2 — รายได้สมาชิกเป็น gate การใช้สิทธิ์ |
| IS-13 | **ค่าสมาชิกรายปี 3 ระดับ** (Normal/Verified/Premium) + ขออัพเกรด tier + ต่ออายุรายปี + auto-renew + แจ้งเตือนก่อนหมดอายุ | รายได้แพลตฟอร์มแยกขาดจาก flow ซื้อขาย (Legal #1) |
| IS-14 | **Free Trial 3 เดือนนับจากวันสมัคร** + เปิดเผยราคา/วันหมด trial ตั้งแต่สมัคร + เตือนล่วงหน้า (14/3/1 วัน) | Legal #8 ใหม่ — คุ้มครองผู้บริโภค เลี่ยงเก็บเงินเงียบ |
| IS-15 | **ระบบ Referral + Credit** แบบ single-level; เครดิตใช้ได้เฉพาะในแพลตฟอร์ม **ถอน/แลกเงิน/โอนไม่ได้**; มีวันหมดอายุ + กัน abuse | Legal #9/#10 ใหม่ — เลี่ยง พ.ร.บ.ขายตรงฯ + พ.ร.บ.ระบบการชำระเงินฯ (e-money) |
| IS-16 | **ใช้เครดิตซื้อตำแหน่งโปรโมต listing** (Featured/Promoted) ตามแพ็กเกจ/ระยะเวลา + credit ledger โปร่งใส | รายได้/สิทธิประโยชน์แพลตฟอร์ม แยกขาดจาก flow ซื้อขาย |
| IS-17 | **ระบบแจ้งเตือน + Notification log** บันทึกว่าแจ้งใคร/เมื่อไหร่/ช่องทางใด/milestone (14/3/1 วัน) แบบกันส่งซ้ำ; เป็นหลักฐานก่อน auto-renew | Legal #8 — พิสูจน์ได้ว่าแจ้งก่อนตัดเงิน (พ.ร.บ.คุ้มครองผู้บริโภค) — ปิด B-01 |
| IS-18 | **Idempotency + atomicity ของเครดิต** ให้รางวัล referral/หักเครดิตโปรโมตกันซ้ำด้วย idempotency key; หักเครดิต+บันทึก ledger+อัปเดต balance ใน transaction เดียว | integrity เครดิต/ledger โปร่งใส (Legal #6) — ปิด B-02 |
| IS-19 | **ข้อมูลความเห็นผู้ประเมินอิสระ** (ผู้ประเมิน/ข้อความ/เวอร์ชัน disclaimer/เวลา) ให้ FR-07 ทำได้ระดับ data + คงข้อห้ามคำว่ารับประกัน/การันตี/ของแท้ 100% | Legal #2 — จัดการความรับผิดจากการรับประกันความแท้ระดับ data — ปิด B-03 |
| IS-20 | **Config versioning + price snapshot** เก็บประวัติเวอร์ชัน ราคา/ค่าสมาชิก/แพ็กเกจ/เครดิต + effective date + snapshot ราคาที่ชำระจริง ลง membership/ใบแจ้งหนี้ | Legal #8 — เปลี่ยนราคาไม่ย้อนหลังรอบที่ชำระแล้ว — ปิด B-04 |
| IS-21 | **State `PendingBanReview`** ในวงจร account/trust (เสนอ ban รอ Admin ยืนยัน — human-in-the-loop) | Legal #6 / DP-3 — ห้าม auto-ban ถาวรล้วน — ปิด B-05 |
| IS-22 | **Append-only ระดับฐานข้อมูล** สำหรับ AuditLogs/ConsentRecords/CreditTransactions (trigger ปฏิเสธ UPDATE/DELETE / DENY permission / temporal) | Legal #6/#7 — immutable ตรวจสอบย้อนหลัง/AML — ปิด B-06 |

### 1.2 Out-of-Scope (ตัดออกจาก MVP)
| # | รายการที่ตัด | เหตุผล/อ้างอิงกฎหมาย |
|---|---|---|
| OS-1 | **Escrow / Wallet / ถือเงินกลาง** ทุกรูปแบบ | Legal #1, #5 — เลี่ยง พ.ร.บ.ระบบการชำระเงิน 2560 (ต้องมีใบอนุญาต) |
| OS-2 | หักค่าคอมมิชชันจากเงินซื้อขาย | Legal #1 — เงินไม่ผ่านระบบ |
| OS-3 | **การรับประกัน/การันตีความแท้** ของสินค้า | Legal #2, #5 — แพลตฟอร์มเป็นตัวกลาง ไม่ใช่ผู้รับประกัน |
| OS-4 | **Pre-Order / ระดมทุน** | Legal #5 — เสี่ยงเข้าข่ายระดมทุน/รับฝากเงินล่วงหน้า |
| OS-5 | เชื่อมต่อ PSP / payment gateway ในระบบ | Legal #1 — เฟสหลังเมื่อมีพันธมิตร PSP มีใบอนุญาต |
| OS-6 | Buyer protection แบบคืนเงินอัตโนมัติ | ขัดกับ no-touch (ระบบไม่ถือเงิน) |
| OS-7 | เก็บภาพบัตรประชาชน/สมุดบัญชีดิบเป็น default | Legal #4 — รับเฉพาะผล NDID |

---

## 2. Actors & Roles

| Actor | คำอธิบาย | สิทธิ์หลัก |
|---|---|---|
| **Guest** | ผู้เยี่ยมชม ยังไม่ login | ดูประกาศ/ค้นหาเท่านั้น; **ห้าม bid/ลงประกาศ** |
| **Normal** | สมาชิกพื้นฐาน (ผ่าน email/phone) | ลงประกาศจำกัด, bid/ซื้อได้ **เฉพาะเมื่อ membership active** (trial หรือชำระแล้ว); ไม่ผ่าน KYC |
| **Verified** | ผ่าน e-KYC (NDID) | สิทธิ์ Normal + เพิ่ม limit + ป้าย Verified + ลงสินค้ามูลค่าสูง (membership active เช่นกัน) |
| **Premium** | Verified + จ่ายค่าสมาชิก Premium | สิทธิ์ Verified + โปรโมต/feature listing + ลดค่าธรรมเนียม (membership active) |
| **Member (สถานะ membership ตัดขวางทุก tier)** | คุณสมบัติ active/trial/expired ที่ผูกกับ Normal/Verified/Premium | กำหนดว่าใช้สิทธิ์ bid/listing ได้หรือไม่; ถือ Credit balance + Referral code |
| **Admin** | เจ้าหน้าที่บริษัท | จัดการผู้ใช้/ดิสพิวต์/อุทธรณ์, ดู audit log, override ลงทัณฑ์ตาม due process, ตั้งค่า config ราคา/เครดิต/แพ็กเกจโปรโมต, ตรวจ abuse referral |
| **ผู้ประเมินอิสระ (Independent Appraiser)** | บุคคล/หน่วยงานภายนอกอิสระ | ให้ "ความเห็นประกอบ" ต่อ listing; **ไม่ใช่การรับประกันของแพลตฟอร์ม** |

---

## 3. Functional Requirements

> รูปแบบ: แต่ละ FR มีรหัส + Acceptance Criteria (Given/When/Then)

### 3.1 หมวด Account / KYC

**FR-01 สมัครสมาชิก Normal**
ผู้ใช้สมัครด้วย email/เบอร์โทร + ตั้งรหัสผ่าน และต้องยอมรับ consent PDPA
- *Given* Guest กรอกข้อมูลครบและกด "ยอมรับนโยบาย PDPA" *When* ส่งฟอร์ม *Then* ระบบสร้าง User สถานะ Normal, Trust Score = 100, บันทึก consent (เวอร์ชัน+timestamp) ลง audit log
- *Given* ยังไม่ติ๊ก consent *When* ส่งฟอร์ม *Then* ระบบปฏิเสธพร้อมข้อความ

**FR-02 ยืนยันตัวตนด้วย e-KYC (NDID)**
ผู้ใช้ยกระดับเป็น Verified ผ่าน NDID โดยระบบรับเฉพาะ "ผลตรวจ verified" ไม่เก็บภาพบัตร
- *Given* Normal เริ่ม KYC *When* NDID ส่งผล verified กลับ *Then* ระบบบันทึกเฉพาะ {idp, ref, ระดับความเชื่อมั่น, ผล=verified, timestamp} และเลื่อนเป็น Verified; **ไม่จัดเก็บภาพบัตร/เลขบัตรเต็ม**
- *Given* NDID ส่งผล failed *Then* คงสถานะ Normal + log เหตุผล

**FR-03 Fallback KYC (กรณีเลี่ยงไม่ได้)**
ถ้าจำเป็นต้องเก็บเอกสารระบุตัวตน ต้อง encrypt-at-rest + masking + ลบหลังอนุมัติ
- *Given* ระบบรับไฟล์เอกสาร *When* บันทึก *Then* เข้ารหัส, แสดงผลแบบ mask (เช่น x-xxxx-xxxxx-12-3), ตั้ง retention job ลบไฟล์ดิบหลังอนุมัติภายใน N วัน + log การลบ

**FR-04 จัดการ consent / สิทธิเจ้าของข้อมูล (PDPA)**
ผู้ใช้ดู/ถอน consent และขอลบ/เข้าถึงข้อมูลตนเองได้
- *Given* ผู้ใช้ login *When* ขอ export/ลบข้อมูล *Then* ระบบสร้างคำขอ, แจ้ง SLA, และ log

**FR-05 ยกระดับ Premium / อัพเกรด tier**
- *Given* Verified จ่ายค่าสมาชิก Premium (ผ่านช่องทางค่าธรรมเนียมที่แยกจาก flow ซื้อขาย) *When* ชำระสำเร็จ *Then* เลื่อนเป็น Premium พร้อมวันหมดอายุ (ผูกกับ membership term ตาม FR-27)
- *Given* สมาชิกขออัพเกรด tier ระหว่างรอบ (เช่น Normal→Verified→Premium) *When* ชำระส่วนต่างตาม config *Then* อัปเดต tier ทันที, คงวันหมดอายุ membership เดิมหรือคำนวณ pro-rate ตาม business rule, log
> หมายเหตุ: รายละเอียดราคา/รอบปี/auto-renew/trial ดู **FR-27 (Membership Lifecycle)**

### 3.2 หมวด Listing สินค้า

**FR-06 สร้างประกาศขาย** *(ปรับ v0.2: ต้องเป็นสมาชิกที่ membership active)*
- *Given* สมาชิก (Normal+) ที่ **membership active** (อยู่ในช่วง Free Trial หรือชำระค่าสมาชิกรายปีแล้ว) *When* กรอกชื่อ/หมวด/รูป/ราคา/เงื่อนไข *Then* สร้าง Product สถานะ Draft→Active หลังผ่าน validation; ระบบแสดง **disclaimer มาตรฐาน** ว่าแพลตฟอร์มเป็นตัวกลาง ไม่รับประกันความแท้
- *Given* ผู้ใช้ที่ **membership หมดอายุ/ยังไม่ชำระหลังพ้น trial** *When* กดสร้างประกาศ *Then* ระบบปฏิเสธพร้อมข้อความ + ลิงก์ไปต่ออายุ/ชำระค่าสมาชิก (เชื่อม FR-27); **การลงทะเบียนอย่างเดียวไม่พอ ต้อง active เท่านั้น**
- *Given* Guest (ยังไม่ login) *Then* ไม่เห็นปุ่มลงประกาศ / ถูกปฏิเสธที่ API
- *Given* มูลค่าสินค้าเกินเกณฑ์ที่กำหนด (**KYC threshold อ่านจาก config ตาม FR-35** — ใช้เวอร์ชันที่ effective ณ เวลาลงประกาศ) *When* ผู้ลงเป็น Normal *Then* ระบบบังคับให้ KYC (เป็น Verified) ก่อน

**FR-07 ความเห็นประกอบจากผู้ประเมินอิสระ**
- *Given* ผู้ขายร้องขอความเห็น *When* ผู้ประเมินอิสระให้ความเห็น *Then* แสดงเป็น "ความเห็นประกอบจากผู้ประเมินอิสระ (ไม่ใช่การรับประกันของแพลตฟอร์ม)" + ระบุตัวผู้ประเมิน + disclaimer
- *Given* ทุกหน้าที่แสดงความเห็น *Then* ระบบ**ห้าม**ใช้คำว่า "รับประกัน/การันตี/ของแท้ 100%"
> หมายเหตุ: โครงสร้างข้อมูลของความเห็น (ผู้ประเมิน, ข้อความ, เวอร์ชัน disclaimer, เวลา) และการบังคับ word-blocklist ดู **FR-34 (Appraisal Opinion Data)**

**FR-08 ค้นหา/เรียกดูประกาศ (Guest ได้)**
- *Given* Guest *When* ค้นหา *Then* เห็นรายการ Active เท่านั้น; ห้าม bid/ติดต่อโดยไม่ login

### 3.3 หมวด Bidding / ประมูล

**FR-09 ตั้งการประมูล**
- *Given* ผู้ขายเลือกขายแบบประมูล *When* ตั้งราคาเริ่ม/ราคาขั้นต่ำ/เวลาปิด *Then* สร้าง Auction สถานะ Open

**FR-10 เสนอราคา (Bid)** *(ปรับ v0.2: ต้องเป็นสมาชิกที่ membership active)*
- *Given* สมาชิก login **และ membership active** (Free Trial หรือชำระค่าสมาชิกรายปีแล้ว) *When* bid > ราคาปัจจุบัน + step *Then* บันทึก Bid, อัปเดตราคาสูงสุด, log
- *Given* ผู้ใช้ login แต่ **membership หมดอายุ/ยังไม่ชำระหลังพ้น trial** *When* กด bid *Then* ระบบปฏิเสธพร้อมข้อความ + ลิงก์ต่ออายุ/ชำระ (เชื่อม FR-27); **ลงทะเบียนอย่างเดียวไม่พอ ต้อง active**
- *Given* Guest (ยังไม่ login) *Then* ห้าม bid (ดู/ค้นหาได้เท่านั้น ตาม FR-08)
- *Given* bid ต่ำกว่าเกณฑ์/หลังเวลาปิด *Then* ปฏิเสธ

**FR-11 Anti–Shill Bidding (กันปั่นราคาด้วยบัญชีพวก)**
- *Given* มีการ bid *When* ตรวจพบรูปแบบต้องสงสัย (เช่น ผู้ bid สัมพันธ์กับผู้ขาย — same device/IP/payment-hint/เครือข่ายบัญชี, bid วน, สร้างบัญชีใหม่เพื่อดันราคา) *Then* ระบบ flag, ตัด bid ต้องสงสัยออกจากการคิดราคาชนะ, แจ้ง Admin, log หลักฐาน
- *Given* ยืนยันว่าปั่นราคา *Then* ลด Trust Score ตาม business rule + เข้ากระบวนการลงทัณฑ์ (FR-15)

**FR-12 ปิดประมูล**
- *Given* ถึงเวลาปิด (Worker/BackgroundService) *When* มีผู้ชนะที่ผ่าน anti-shill *Then* ตั้งผู้ชนะ, เปลี่ยน Auction เป็น Closed, แจ้งคู่ดีล, เปิด flow โอนตรง (FR-18)

### 3.4 หมวด Trust Score & ลงทัณฑ์

**FR-13 เริ่มต้น Trust Score**
- *Given* สร้างบัญชีใหม่ *Then* Trust Score = 100, บันทึก TrustScoreHistory แถวแรก (reason=initial)

**FR-14 ปรับ Trust Score จากเหตุการณ์**
- *Given* เกิดเหตุการณ์ (ลหุโทษ/รีวิว/ผิดนัด) *When* ระบบประเมิน *Then* ปรับคะแนน (ลหุโทษ = -20) + บันทึก TrustScoreHistory {delta, reason, refId, actor, timestamp}

**FR-15 ลงทัณฑ์อัตโนมัติ + Due Process**
- *Given* Trust Score < 60 *Then* สถานะ → Suspended (ชั่วคราว) + แจ้งเหตุผลเป็นถ้อยคำกลาง + **เปิดสิทธิอุทธรณ์**
- *Given* ความผิดอุกฉกรรจ์ (ฉ้อโกง/ของผิดกฎหมาย) *Then* เสนอ Ban + ขึ้น Blacklist ภายใน — **ต้องผ่านการยืนยันของ Admin (human-in-the-loop) ก่อนมีผลถาวร**
- *Given* ผู้ใช้ยื่นอุทธรณ์ภายในกำหนด *When* Admin พิจารณา *Then* บันทึกผล (ยืน/กลับ/ลดโทษ) + คืนสถานะถ้าชนะ + log ทุกขั้นตอน

**FR-16 ดูประวัติ Trust Score ของตนเอง**
- *Given* ผู้ใช้ login *Then* เห็นคะแนนปัจจุบัน + ประวัติการเปลี่ยนแปลง + เหตุผล (โปร่งใส รองรับ due process)

### 3.5 หมวด Blacklist / ระบบเตือน

**FR-17 ระบบเตือนคู่ธุรกรรม (Warn-on-deal)**
- *Given* ผู้ใช้ A กำลังจะทำดีลกับผู้ใช้ B และ B อยู่ใน Blacklist ภายใน *When* เปิดหน้าก่อนตกลงดีล *Then* แสดงคำเตือน**เฉพาะแก่ A**ด้วยถ้อยคำกลาง ("คู่ค้ารายนี้ถูกระงับเนื่องจากละเมิดข้อกำหนดข้อ X") **ไม่เปิดเผยรายละเอียดที่ทำให้เสียชื่อเสียง**
- *Given* บุคคลที่สามที่ไม่ได้กำลัง deal *Then* **ไม่เห็น**สถานะ Blacklist (ไม่ใช่รายชื่อประจานสาธารณะ)
- *Given* ผู้ถูกขึ้น Blacklist *Then* มีสิทธิ์ได้รับแจ้งเหตุผล + ช่องทางอุทธรณ์ (เชื่อม FR-15)

### 3.6 หมวด โอนตรง / ปิดดีล (no-touch)

**FR-18 โอนตรงเคร่งครัด (No-touch Payment)**
- *Given* ดีลปิด *When* ระบบเปิดหน้าชำระเงิน *Then* ระบบ**แสดงเพียงข้อมูลบัญชีผู้ขายให้ผู้ซื้อโอนตรง** + ข้อความว่า "แพลตฟอร์มไม่รับ ถือ หรือเป็นตัวกลางการเงิน"; **ระบบไม่สร้าง wallet, ไม่รับเงิน, ไม่หักค่าคอม**
- *Given* ทุกหน้าชำระเงิน *Then* ระบบไม่มี endpoint ที่รับยอดเงินซื้อขายเข้าบัญชีบริษัท (ตรวจได้จาก architecture)

**FR-19 ยืนยันการรับของ + รีวิว**
- *Given* ผู้ซื้อได้รับสินค้า *When* กด "ยืนยันรับของ" + ให้รีวิว/ดาว *Then* ปิดธุรกรรม, trigger ปรับ Trust Score ทั้งสองฝ่าย (FR-14), log

### 3.7 หมวด Dispute / รายงานปัญหา

**FR-20 เปิดข้อพิพาท**
- *Given* คู่ดีลมีปัญหา (ไม่ได้ของ/ของไม่ตรง/ไม่โอน) *When* ยื่นรายงานพร้อมหลักฐาน *Then* สร้าง Dispute สถานะ Open, แจ้ง Admin, freeze การปรับ Trust Score อัตโนมัติของกรณีนั้นจนกว่าจะตัดสิน

**FR-21 ตัดสินข้อพิพาท (ตัวกลาง ไม่ใช่ผู้รับประกัน)**
- *Given* Admin พิจารณา *When* ตัดสิน *Then* บันทึกผล + เหตุผล + ปรับ Trust Score/ลงทัณฑ์ตาม due process; **ระบบไม่จ่าย/คืนเงินแทน** (no-touch) แต่ให้คำแนะนำ/บันทึกเป็นหลักฐาน

### 3.8 หมวด ค่าธรรมเนียม / สมาชิก

**FR-22 เรียกเก็บค่าธรรมเนียมแพลตฟอร์ม**
- *Given* ผู้ใช้สมัคร/ต่ออายุ Premium หรือซื้อ slot ลงประกาศ *When* ชำระ *Then* บันทึกเป็นรายได้แพลตฟอร์มในช่องทางที่**แยกขาดจาก flow ซื้อขายระหว่างผู้ใช้** (Legal #1)
- *Given* ตรวจสอบ *Then* ไม่มีความเชื่อมโยงระหว่างค่าธรรมเนียมกับมูลค่าธุรกรรมซื้อขาย (ไม่ใช่ % commission)

**FR-23 ค่าลงประกาศ / โปรโมต**
- *Given* สมาชิกซื้อ slot/โปรโมต *Then* บันทึกธุรกรรมค่าธรรมเนียม + ใบเสร็จ

### 3.9 หมวด Admin / Audit

**FR-24 Audit Log ทุกธุรกรรม**
- *Given* เกิด action สำคัญ (login, KYC, listing, bid, deal, payment-info-view, trust change, blacklist, dispute, admin override) *Then* เขียน audit log แบบ append-only {actor, action, target, before/after, ip, timestamp} เก็บแบบ AML-friendly
- *Given* พยายามแก้/ลบ log *Then* ระบบปฏิเสธ (immutable/append-only)

**FR-25 Admin จัดการ due process**
- *Given* มีการลงทัณฑ์/อุทธรณ์/ดิสพิวต์ *When* Admin ดำเนินการ *Then* ทุกการตัดสินถูก log พร้อมเหตุผลและตัวผู้ตัดสิน

**FR-26 รายงาน AML-friendly**
- *Given* Admin/compliance ขอ *Then* ดึงรายงานธุรกรรม/พฤติกรรมต้องสงสัยได้ (รองรับการตรวจสอบภายหลัง)

### 3.10 หมวด Membership / Free Trial *(ใหม่ v0.2)*

> หลักการ: ค่าสมาชิก/ค่าโปรโมต/เครดิต ทั้งหมดเป็น **รายได้แพลตฟอร์มที่แยกขาดจาก flow เงินซื้อขายระหว่างผู้ใช้ (no-touch)** — ไม่ใช่ % commission จากธุรกรรม (คงหลัก Legal #1)

**FR-27 วงจรชีวิตสมาชิก: ค่าสมาชิกรายปี + Free Trial + ต่ออายุ/Auto-renew**
ค่าสมาชิกรายปี 3 ระดับ (ค่าตั้งต้น สมมติฐาน ตั้งได้ใน config): **Normal = 1,000 / Verified = 1,500 / Premium = 2,000 บาท/ปี**. สมาชิกใหม่ทุกคนได้ **Free Trial 3 เดือนนับจากวันสมัคร**.
- *Given* Guest สมัครสมาชิกใหม่ (FR-01) *When* สร้างบัญชีสำเร็จ *Then* ระบบตั้ง MembershipStatus = TRIAL, trialStart = วันสมัคร, trialEnd = trialStart + 3 เดือน, membership = active ตลอดช่วง trial และ **แสดงราคาค่าสมาชิกรายปีของ tier + วันหมด trial ให้เห็นชัดตั้งแต่ตอนสมัคร** (เลี่ยงเก็บเงินเงียบ — คุ้มครองผู้บริโภค)
- *Given* สมาชิกอยู่ในช่วง trial *Then* ใช้สิทธิ์สมาชิก (bid/listing ตาม FR-06/FR-10) ได้ตามปกติ
- *Given* ใกล้หมด trial หรือใกล้หมดรอบปี *When* เหลือ **14 / 3 / 1 วัน** *Then* ระบบส่งการแจ้งเตือนล่วงหน้า (email/in-app) ระบุราคา + วันหมด + วิธีชำระ/ปิด auto-renew + **บันทึก Notification log ตาม FR-32** (กันส่งซ้ำ milestone เดียวกัน)
- *Given* trial หมดอายุและยังไม่ชำระค่าสมาชิกรายปี *When* ถึง trialEnd *Then* MembershipStatus → EXPIRED, **ระงับสิทธิ์ bid/listing** (ดู/ค้นหายังได้), ไม่มีการตัดเงินอัตโนมัติถ้าไม่ได้เปิด auto-renew/ไม่มีวิธีชำระที่ยินยอม
- *Given* สมาชิกชำระค่าสมาชิกรายปี (ช่องทางค่าธรรมเนียมแยกจาก flow ซื้อขาย) *When* ชำระสำเร็จ *Then* MembershipStatus → ACTIVE, paidUntil = วันชำระ + 1 ปี (ปีต่อปี), ออกใบเสร็จ, log เป็นรายได้แพลตฟอร์ม
- *Given* สมาชิกเปิด **auto-renew** และมีวิธีชำระที่ยินยอมไว้ (**payment mandate ตาม FR-35** — เงินค่าบริการบริษัท ไม่ใช่ flow ซื้อขาย) *When* ถึงวันหมดรอบ *Then* ระบบต่ออายุอัตโนมัติ **เฉพาะเมื่อมีหลักฐานว่าได้แจ้งเตือนล่วงหน้าครบแล้ว (ตรวจจาก Notification log FR-32)** + คิดราคาตาม **price snapshot/เวอร์ชัน config ที่ effective (FR-35)** + สามารถปิด auto-renew ได้ทุกเมื่อก่อนรอบตัด
- *Given* ถึงวันหมดรอบแต่ **ไม่มีหลักฐานการแจ้งเตือนครบใน Notification log (FR-32)** *Then* ระบบ**ห้าม**ตัดเงิน auto-renew (precondition กฎหมายคุ้มครองผู้บริโภคไม่ครบ) + แจ้ง Admin/retry แจ้งเตือน
- *Given* สมาชิกปิด auto-renew หรือไม่มีวิธีชำระ *When* ถึงวันหมดรอบ *Then* ไม่ตัดเงิน, สถานะ → EXPIRED ตามปกติ
- *Acceptance (no-touch):* ค่าสมาชิกบันทึกแยกขาดจากธุรกรรมซื้อขาย ไม่ผูกกับมูลค่าดีลใด ๆ

**FR-28 ระบบ Referral Code + Credit (Single-level, non-cashable)**
ตอนสมัครกรอก referral code ได้; ให้เครดิตทั้งผู้แนะนำและผู้ถูกแนะนำ (จำนวนตั้งใน config).
- *Given* ผู้สมัครใหม่กรอก referral code ที่ valid ของสมาชิกที่มีอยู่ *When* สมัครสำเร็จและผ่านเงื่อนไข trigger ตาม config (เช่น เมื่อชำระค่าสมาชิกครั้งแรก เพื่อกัน abuse) *Then* ให้เครดิตแก่ **ผู้แนะนำ (referrer)** และ **ผู้ถูกแนะนำ (referee)** ตามจำนวนใน config + บันทึกลง Credit Ledger {userId, delta, reason=referral, refId, expireAt, timestamp} แบบ **idempotent ตาม FR-33** (กันให้รางวัลซ้ำเมื่อ retry/worker รันซ้ำ)
- *Given* การคิดเครดิตจากการแนะนำ *Then* ระบบให้เครดิต **เฉพาะชั้นเดียว (single-level)** — referrer ได้จากผู้ที่ตนชวนตรงเท่านั้น **ห้ามจ่ายเป็นชั้น/ทอด (multi-level)** เด็ดขาด (เลี่ยงเข้าข่ายแชร์ลูกโซ่/ขายตรง ตาม พ.ร.บ.ขายตรงและตลาดแบบตรง)
- *Given* ผู้ใช้ถือเครดิต *Then* เครดิต **ใช้ได้เฉพาะบริการในแพลตฟอร์ม (เช่น ค่าสมาชิก/ค่าโปรโมต) เท่านั้น — ถอน/แลกเป็นเงินสดไม่ได้ และโอนให้ผู้ใช้อื่นไม่ได้** (เลี่ยงเข้าข่าย e-money ตาม พ.ร.บ.ระบบการชำระเงิน 2560); ระบบไม่มี endpoint ถอน/โอนเครดิต (ตรวจได้จาก architecture)
- *Given* เครดิตถูกออกให้ *Then* มี **วันหมดอายุ (expireAt)** ตาม config; เมื่อหมดอายุ ระบบหักออกจาก balance + log (reason=expired)
- *Given* ตรวจพบพฤติกรรม abuse (self-referral, บัญชีปลอม/ซ้ำ device/IP/KYC เดียวกัน, สมัครเพื่อเก็บเครดิตแล้วทิ้ง) *When* ระบบ/Admin ตรวจพบ *Then* ระงับ/ริบเครดิตที่ได้มาโดยมิชอบ + flag บัญชี + เชื่อมกระบวนการลงทัณฑ์ (FR-15) + log หลักฐาน
- *Acceptance (no-touch):* เครดิตเป็นสิทธิประโยชน์ภายใน ไม่ใช่เงินฝาก/เงินอิเล็กทรอนิกส์ และไม่เกี่ยวกับเงินซื้อขายระหว่างผู้ใช้

**FR-29 Credit Ledger (บัญชีเครดิตโปร่งใส)**
- *Given* มีการได้/ใช้/หมดอายุ/ริบเครดิต *Then* เขียนรายการลง Credit Ledger แบบ append-only {userId, type(earn/spend/expire/revoke), amount, balanceAfter, reason, refId, actor, timestamp}
- *Given* ผู้ใช้ login *Then* เห็น balance ปัจจุบัน + ประวัติรายการ + วันหมดอายุของแต่ละก้อน (โปร่งใส รองรับการตรวจสอบ/ร้องเรียน)
- *Given* พยายามแก้/ลบ ledger *Then* ระบบปฏิเสธ (immutable/append-only เชื่อม NFR-A1)

**FR-30 ใช้เครดิตซื้อตำแหน่งโปรโมต Listing (Featured/Promoted)**
- *Given* สมาชิก membership active เลือกแพ็กเกจโปรโมต (featured/ดันขึ้นบน/ไฮไลต์) สำหรับ listing ของตน ตามแพ็กเกจ/ระยะเวลาใน config *When* ยืนยันและมีเครดิตพอ *Then* หักเครดิต (spend ใน Credit Ledger FR-29) แบบ **idempotent + atomic ตาม FR-33** (กันหักซ้ำเมื่อดับเบิลคลิก/retry; การหักเครดิต+ledger+balance อยู่ใน transaction เดียว), ตั้ง promotion สถานะ Active พร้อม start/end ตามระยะเวลา, แสดง listing ในตำแหน่งโปรโมตในช่วงเวลานั้น
- *Given* เครดิตไม่พอ *Then* ปฏิเสธ + เสนอช่องทางได้เครดิตเพิ่ม (referral/ชำระ) — **ไม่หักจากเงินซื้อขาย**
- *Given* promotion หมดอายุ (Worker/BackgroundService) *Then* คืน listing สู่ลำดับปกติ + log
- *Given* ทุกการโปรโมต *Then* มีบันทึก ledger + audit log โปร่งใส (กันการลำเอียง/ตรวจสอบได้)

**FR-31 ตั้งค่า config ราคา/เครดิต/แพ็กเกจ (Admin)**
- *Given* Admin *When* ตั้ง/แก้ค่าสมาชิกรายปีต่อ tier, ระยะ trial, จำนวนเครดิต referral (referrer/referee), วันหมดอายุเครดิต, แพ็กเกจ/ราคาโปรโมต, ตารางวันแจ้งเตือน (14/3/1), **KYC threshold (FR-06)** *Then* บันทึกค่าใหม่พร้อม effective date + เก็บประวัติเวอร์ชัน config + log; **การเปลี่ยนราคาไม่ย้อนหลังกับรอบ/สิทธิ์ที่ชำระไปแล้ว**
> หมายเหตุ: กลไกเก็บประวัติเวอร์ชัน + effective date + price snapshot ที่บังคับ "ไม่ย้อนหลัง" ในระดับ data ดู **FR-35 (Config Versioning & Price Snapshot)**

### 3.11 หมวด Notification / แจ้งเตือน *(ใหม่ v0.3 — ปิด B-01)*

**FR-32 ระบบแจ้งเตือน + Notification Log (กันส่งซ้ำ + เป็นหลักฐานก่อน auto-renew)**
ทุกการแจ้งเตือนที่มีนัยกฎหมาย (โดยเฉพาะ trial/รอบสมาชิกใกล้หมด milestone **14 / 3 / 1 วัน**) ต้องถูกบันทึกลง Notification log ว่า **แจ้งใคร / เมื่อไหร่ / ช่องทางใด / milestone ใด** และต้อง **กันการส่งซ้ำ** ของ milestone เดียวกัน. auto-renew (FR-27) ทำได้เฉพาะเมื่อ "มีหลักฐานว่าได้แจ้งเตือนล่วงหน้าครบแล้ว" (เงื่อนไขกฎหมายคุ้มครองผู้บริโภค).
- *Given* Worker/BackgroundService สแกนพบ membership/trial ที่เหลือ 14/3/1 วัน *When* ถึง milestone ที่ยังไม่เคยแจ้ง *Then* ส่งแจ้งเตือน (email/in-app) + บันทึก Notification {userId, type (เช่น TrialExpiring/RenewalDue), channel, relatedMembershipId, milestone (14/3/1), scheduledFor, sentAtUtc, status} ลง log
- *Given* milestone เดียวกัน (userId + type + relatedMembershipId + milestone) เคยถูกบันทึกส่งแล้ว *When* worker รันซ้ำ/retry/ดับเบิล trigger *Then* ระบบ**ไม่ส่งซ้ำ** (บังคับด้วย unique key (UserId, Type, MembershipId, Milestone)) — กัน notification ซ้ำ
- *Given* ระบบจะ auto-renew membership (FR-27) *When* ตรวจ precondition *Then* ต้องพบ record การแจ้งครบทุก milestone ที่กำหนดใน Notification log ก่อน จึงตัดเงินได้; **ถ้าไม่ครบ → ห้าม auto-renew**
- *Given* การส่งแจ้งเตือนล้มเหลว (เช่น email bounce) *When* บันทึก *Then* status = Failed + เปิดให้ retry; **ห้ามถือว่าแจ้งสำเร็จจนกว่า status = Sent**
- *Acceptance (no-touch):* Notification log เป็นเรื่องบริการ/สื่อสารกับสมาชิก ไม่เกี่ยวกับเงินซื้อขายระหว่างผู้ใช้

### 3.12 หมวด Credit Integrity *(ใหม่ v0.3 — ปิด B-02)*

**FR-33 Idempotency & Atomicity ของเครดิต (กันให้ซ้ำ/หักซ้ำ)**
การให้รางวัล referral (FR-28) และการหักเครดิตโปรโมต (FR-30) ทุกครั้งต้อง **idempotent** (กันให้ซ้ำ/หักซ้ำเมื่อ retry / ดับเบิลคลิก / worker รันซ้ำ) ด้วย **idempotency key**; และการหักเครดิต + บันทึก ledger (FR-29) + อัปเดต balance ต้องอยู่ใน **transaction เดียว (atomic)**.
- *Given* operation เครดิตใด ๆ (earn/spend/expire/revoke) มาพร้อม idempotency key (เช่น Type+RefId หรือ IdempotencyKey เฉพาะ) *When* คีย์นี้เคยถูกบันทึกลง ledger สำเร็จแล้ว *Then* ระบบ**ไม่ทำซ้ำ** และคืนผลลัพธ์เดิม (no double-credit / no double-debit) — บังคับด้วย **UNIQUE constraint** บน ledger
- *Given* ผู้ใช้ดับเบิลคลิก "โปรโมต" หรือ client retry *When* ส่งคำขอเดิมซ้ำด้วยคีย์เดียวกัน *Then* หักเครดิตเพียงครั้งเดียว
- *Given* การหักเครดิตโปรโมต *When* ดำเนินการ *Then* การ {insert ledger spend, update CreditAccount.balance, ผูก ListingPromotion.CreditTransactionId} อยู่ใน **DB transaction เดียว** — ถ้าขั้นใดล้มเหลว rollback ทั้งหมด (ไม่มีสภาพหักเงินแต่ promotion ไม่เกิด หรือกลับกัน)
- *Given* worker คำนวณ referral reward รันซ้ำ (เช่น crash แล้ว resume) *When* reward ของคู่ referrer/referee นั้นเคยออกแล้ว *Then* ไม่ออกซ้ำ (idempotent ตาม refId ของ referral)
- *Given* ความสัมพันธ์ ListingPromotion ↔ CreditTransaction *Then* หลังหักเครดิตสำเร็จ ต้อง NOT NULL + UNIQUE (1 promotion ผูก 1 spend) เพื่อกันผูกซ้ำ/ไม่ผูก ledger
- *Acceptance (no-touch):* idempotency/atomicity ใช้กับเครดิตซึ่งเป็นสิทธิประโยชน์ภายใน ไม่เกี่ยวกับเงินซื้อขาย

### 3.13 หมวด Appraisal Data *(ใหม่ v0.3 — ปิด B-03)*

**FR-34 ข้อมูลความเห็นผู้ประเมินอิสระ (Appraisal Opinion Data)**
ให้ FR-07 ทำได้จริงในระดับข้อมูล โดยเก็บความเห็นของผู้ประเมินอิสระแบบมีโครงสร้าง พร้อมคงข้อห้ามคำที่สื่อการรับประกัน.
- *Given* ผู้ประเมินอิสระให้ความเห็นต่อ listing *When* บันทึก *Then* ระบบเก็บ {productId, appraiserId/appraiserName, opinionText, disclaimerVersion, createdAtUtc} และผูกกับ listing นั้น
- *Given* บันทึก/แสดงความเห็น *When* opinionText มีคำต้องห้าม ("รับประกัน" / "การันตี" / "ของแท้ 100%" หรือถ้อยคำสื่อความหมายเดียวกัน) *Then* ระบบ**ปฏิเสธการบันทึก/แสดง** (บังคับ word-blocklist ระดับ validation) + แจ้งเหตุผล
- *Given* ทุกการแสดงความเห็น *Then* แนบ disclaimer เวอร์ชันที่บันทึกไว้ ("ความเห็นประกอบจากผู้ประเมินอิสระ — ไม่ใช่การรับประกันของแพลตฟอร์ม") + ระบุตัวผู้ประเมิน
- *Given* มีการแก้ disclaimer มาตรฐาน *When* ผู้ประเมินให้ความเห็นใหม่ *Then* บันทึก disclaimerVersion ปัจจุบัน (กันความเห็นเก่าอ้าง disclaimer ที่เปลี่ยนไป — รองรับตรวจย้อนหลัง Legal #2)

### 3.14 หมวด Config Versioning *(ใหม่ v0.3 — ปิด B-04)*

**FR-35 Config Versioning & Price Snapshot (ไม่ย้อนหลังรอบที่ชำระแล้ว)**
ราคา/ค่าสมาชิก/แพ็กเกจ/จำนวนเครดิต/KYC threshold (FR-31) ต้องเก็บ **ประวัติเวอร์ชัน + effective date**; และเมื่อออกบิล/ผูกสิทธิ์ ต้อง **snapshot ราคาที่ชำระจริง** ลงที่ membership/ใบแจ้งหนี้ เพื่อบังคับ "เปลี่ยนราคาไม่ย้อนหลังรอบที่ชำระแล้ว".
- *Given* Admin แก้ค่า config (FR-31) *When* บันทึก *Then* สร้างเวอร์ชันใหม่ {configKey, value, effectiveFromUtc, createdByUserId, version} โดย**ไม่ทับเวอร์ชันเดิม** (เก็บประวัติทั้งหมด) + log
- *Given* ระบบออกบิล/ผูกสิทธิ์ (membership term, upgrade, promotion, auto-renew) *When* ดำเนินการ *Then* อ่านค่า config เวอร์ชันที่ **effective ณ เวลานั้น** + เขียน **snapshot ราคาที่ชำระจริง** (เช่น `PaidAmountTHB`, `PricedConfigVersion`) ลง Membership/FeeInvoice
- *Given* Admin เปลี่ยนราคาภายหลัง *When* มีรอบ/สิทธิ์ที่ชำระไปแล้วก่อน effective date *Then* รอบเดิม**ใช้ราคา snapshot เดิม** ไม่ถูกกระทบ (บังคับด้วย snapshot ไม่ใช่ join lookup ปัจจุบัน)
- *Given* ตรวจสอบย้อนหลัง *When* ถามว่าใครชำระราคาเท่าไรตอนไหน *Then* ตอบได้จาก snapshot + เวอร์ชัน config ที่ effective (โปร่งใส รองรับผู้บริโภค/ตรวจสอบ)
- *Given* auto-renew (FR-27) *Then* คิดราคาตามเวอร์ชัน config ที่ effective ณ วันต่ออายุ + บันทึก snapshot ใหม่ของรอบนั้น
- *Acceptance:* payment mandate ของ auto-renew (FR-27 / Y-03) ผูกกับ config นี้ และเป็นเงินค่าบริการบริษัท ไม่ใช่ flow ซื้อขาย (คง no-touch)

### 3.15 หมวด Account State *(ใหม่ v0.3 — ปิด B-05)*

**FR-36 State `PendingBanReview` (human-in-the-loop ก่อน ban ถาวร)**
เพิ่ม state `PendingBanReview` ในวงจร account/trust (§6.2) เพื่อแยก "เสนอ ban (รอ Admin)" ออกจาก "ban แล้ว" ให้ตรง DP-3.
- *Given* เกิดความผิดอุกฉกรรจ์ (ฉ้อโกง/ของผิดกฎหมาย ตาม BR-5) *When* ระบบเสนอ ban *Then* AccountStatus → **PendingBanReview** (ยังไม่ banned ถาวร) + ระงับสิทธิ์ใช้งานชั่วคราว + แจ้งผู้ใช้ + เปิดสิทธิอุทธรณ์
- *Given* อยู่ใน PendingBanReview *When* **Admin ยืนยัน** (human-in-the-loop) *Then* → **Banned + Blacklisted** + log ผู้ตัดสิน/เหตุผล
- *Given* อยู่ใน PendingBanReview *When* **Admin ปฏิเสธ** หรือ **อุทธรณ์สำเร็จ** *Then* → **Active** + คืนสิทธิ์ + log
- *Given* state machine *Then* **ห้าม** transition จากเหตุการณ์อัตโนมัติไปสู่ Banned โดยตรงโดยไม่ผ่าน PendingBanReview (บังคับ DP-3 ในระดับ state) — ค่า AccountStatus ที่อนุญาต = {Active, Suspended, PendingBanReview, Banned}

### 3.16 หมวด Data Immutability *(ใหม่ v0.3 — ปิด B-06)*

**FR-37 บังคับ Append-only ระดับฐานข้อมูล**
AuditLogs (FR-24), ConsentRecords (FR-01/04), CreditTransactions (FR-29) ต้องเป็น append-only/immutable **ที่ระดับฐานข้อมูล** ไม่ใช่แค่ระดับ app (สอดคล้อง NFR-A4).
- *Given* มี client/role ใด ๆ (รวมถึง app account) พยายาม UPDATE หรือ DELETE แถวใน AuditLogs/ConsentRecords/CreditTransactions *When* คำสั่งถูกส่งถึง DB *Then* ฐานข้อมูล**ปฏิเสธ** (เช่น INSTEAD OF UPDATE/DELETE trigger ที่ raise error, หรือ DENY UPDATE/DELETE บน role ของ app, หรือ system-versioned temporal/ledger table)
- *Given* การแก้ไขเชิงตรรกะที่จำเป็น (เช่น เครดิตหมดอายุ/ริบ) *Then* ทำด้วยการ **insert แถวใหม่** (type=expire/revoke) ไม่ใช่ update แถวเดิม (คง append-only)
- *Given* audit/ตรวจสอบ *Then* พิสูจน์ได้ว่าไม่มีการแก้/ลบย้อนหลัง (รองรับ AML/due process แม้มีการเข้าถึง DB ตรง)

---

## 4. User Flow หลัก

```
[สมัคร + (กรอก referral code?)] -> [ยอมรับ consent PDPA] -> [แสดงราคา + วันหมด Free Trial 3 เดือน]
   -> [membership = TRIAL active] -> (ต้องการสิทธิสูง?) -> [e-KYC NDID -> Verified]
   -> {gate: membership active?} -> [ลงประกาศ + disclaimer + (ขอความเห็นผู้ประเมินอิสระ)] -> [(ใช้เครดิตโปรโมต listing?)]
   -> {gate: membership active?} -> [ประมูล/Bid] -> [anti-shill check] -> [ปิดประมูล: ได้ผู้ชนะ]
   -> [ปิดดีล] -> [โอนตรง no-touch: แสดงบัญชีผู้ขาย, แพลตฟอร์มไม่ถือเงิน]
   -> [ผู้ซื้อยืนยันรับของ] -> [รีวิว 2 ฝ่าย] -> [อัปเดต Trust Score] -> [audit log]
                                   |
                                   +--(มีปัญหา)--> [Dispute] -> [Admin ตัดสิน + due process]
```

จุดควบคุมกฎหมายบน flow:
- ตอนสมัคร -> **เปิดเผยราคา + วันหมด Free Trial ตั้งแต่แรก** (FR-27, คุ้มครองผู้บริโภค) + referral ต้อง single-level (FR-28)
- ก่อน bid/ลงประกาศ -> **ตรวจ membership active** (trial/ชำระแล้ว — FR-06/FR-10/FR-27) + ตรวจ consent + (เกินมูลค่า -> บังคับ KYC)
- ก่อนหมด trial/รอบปี -> **แจ้งเตือนล่วงหน้า 14/3/1 วัน + บันทึก Notification log กันส่งซ้ำ** ก่อนตัด/auto-renew (FR-27/FR-32); auto-renew ตัดเงินได้เฉพาะเมื่อมีหลักฐานแจ้งครบ
- ใช้/ได้เครดิต -> เครดิต **single-level, ถอน/แลก/โอนไม่ได้** ผ่าน ledger โปร่งใส **+ idempotent/atomic กันให้-หักซ้ำ** (FR-28/29/30/33)
- คิดราคา/ออกบิล -> ใช้ **config เวอร์ชันที่ effective + snapshot ราคาที่ชำระจริง** ไม่ย้อนหลัง (FR-35)
- ก่อนปิดดีล -> **warn-on-deal** ถ้าคู่ค้าอยู่ Blacklist (FR-17)
- ขั้นโอนเงิน -> no-touch เท่านั้น (FR-18); ค่าสมาชิก/เครดิต/โปรโมตแยกขาดจากเงินซื้อขาย (FR-22/27/30)
- ทุกขั้น -> audit log (FR-24) + credit ledger append-only (FR-29)

---

## 5. Non-Functional Requirements

### 5.1 Security
- NFR-S1: รหัสผ่าน hash (bcrypt/Argon2); auth ผ่าน JWT + refresh; rate-limit endpoints สำคัญ
- NFR-S2: ข้อมูลอ่อนไหว (ผล KYC, เอกสาร fallback) encrypt-at-rest (column/TDE) + encrypt-in-transit (TLS)
- NFR-S3: RBAC ตาม role; endpoint การเงิน "ห้ามรับเงินซื้อขาย" ตรวจสอบในระดับ architecture/code review
- NFR-S4: ป้องกัน OWASP Top 10 (SQLi via EF param, XSS, CSRF, IDOR)

### 5.2 PDPA / Privacy
- NFR-P1: เก็บ consent มีเวอร์ชัน + เวลา; รองรับถอน consent
- NFR-P2: Data minimization — ไม่เก็บภาพบัตร/บัญชีดิบเป็น default (รับผล NDID); fallback ต้อง mask + retention + ลบอัตโนมัติ
- NFR-P3: รองรับสิทธิเจ้าของข้อมูล (เข้าถึง/แก้ไข/ลบ/พกพา) ตาม SLA
- NFR-P4: Blacklist/Trust ใช้ถ้อยคำกลาง ไม่เปิดเผยเกินจำเป็น (ลดเสี่ยงหมิ่นประมาท/PDPA)

### 5.3 Performance
- NFR-PF1: API p95 < 500ms สำหรับ read ทั่วไป (ภายใต้โหลด MVP)
- NFR-PF2: ปิดประมูลโดย Worker ต้องประมวลผลภายใน <= 5 วินาทีหลังเวลาปิด
- NFR-PF3: รองรับ concurrent bid บน listing เดียวกันด้วย optimistic concurrency / row version

### 5.4 Audit / Logging
- NFR-A1: Audit log append-only, immutable, retain ตามเกณฑ์ AML/บัญชี
- NFR-A2: Log มี correlation id ต่อธุรกรรม; แยก audit log ออกจาก application log
- NFR-A3: เข้าถึง log จำกัดสิทธิ + การเข้าถึง log เองก็ถูก log
- **NFR-A4 (ใหม่ v0.3 — ปิด B-06):** Append-only/immutability ของ `AuditLogs`, `ConsentRecords`, `CreditTransactions` ต้อง**บังคับที่ระดับฐานข้อมูล** ไม่ใช่แค่ระดับ app — ใช้กลไกใดกลไกหนึ่งหรือผสม: (a) INSTEAD OF UPDATE/DELETE trigger ที่ปฏิเสธ, (b) DENY UPDATE/DELETE permission บน role ที่ app ใช้, (c) system-versioned temporal table / ledger table (SQL Server 2022). การเปลี่ยนเชิงตรรกะ (expire/revoke เครดิต) ทำด้วยการ insert แถวใหม่เท่านั้น (เชื่อม FR-37, FR-24, FR-29)

---

## 6. Business Rules — Trust Score & ลงทัณฑ์ (State Machine)

### 6.1 กติกาคะแนน
| Rule | รายละเอียด |
|---|---|
| BR-1 | เริ่มต้น = 100 |
| BR-2 | ลหุโทษ (ผิดนัด/รีวิวลบยืนยันแล้ว/พฤติกรรมเสี่ยงระดับเบา) = -20 ต่อครั้ง |
| BR-3 | shill bidding ที่ยืนยันแล้ว = ลงทัณฑ์ (อย่างน้อยลหุโทษ; ซ้ำ = ยกระดับ) |
| BR-4 | < 60 -> Suspended (ชั่วคราว) |
| BR-5 | อุกฉกรรจ์ (ฉ้อโกง/ของผิดกฎหมาย) -> เสนอ Ban + Blacklist (ต้อง Admin ยืนยัน) |
| BR-6 | คะแนนฟื้นได้จากพฤติกรรมดี/ครบกำหนด suspend (กำหนดสูตรฟื้นในเฟสถัดไป) |

### 6.2 State Machine
> `AccountStatus` ที่อนุญาต = **{Active, Suspended, PendingBanReview, Banned}** (บังคับด้วย CHECK constraint — เพิ่ม `PendingBanReview` ใน v0.3 เพื่อปิด B-05 / รองรับ FR-36 + DP-3). **ห้าม** transition อัตโนมัติจาก Active/Suspended ไป Banned โดยตรง — ต้องผ่าน PendingBanReview ที่ Admin ยืนยันเสมอ (human-in-the-loop).
```
            +-------------------- appeal success ---------------------+
            |                                                         |
        [Active] --score<60--> [Suspended] --appeal/หมดเวลา--> [Active]
            |                       |
            | อุกฉกรรจ์ (เสนอ)        | อุกฉกรรจ์ (เสนอ)
            v                       v
        [PendingBanReview] --Admin ยืนยัน (human-in-the-loop)--> [Banned + Blacklisted]
            |                                       |
            +-- Admin ปฏิเสธ / อุทธรณ์สำเร็จ -> [Active] <-- appeal success
```
- Transition เข้า PendingBanReview เกิดจากเหตุการณ์อุกฉกรรจ์ (BR-5) เท่านั้น และ**ระงับสิทธิ์ชั่วคราว**ระหว่างรอพิจารณา
- ออกจาก PendingBanReview ได้ 2 ทาง: Admin ยืนยัน → Banned+Blacklisted (ถาวร) / Admin ปฏิเสธหรืออุทธรณ์สำเร็จ → Active (รายละเอียดดู FR-36)

### 6.3 Due Process (บังคับ)
- DP-1: ทุกการลงทัณฑ์ต้องมีเหตุผลอ้างอิงข้อกำหนด (ข้อ X) + แจ้งผู้ใช้
- DP-2: Suspended/Ban เปิดสิทธิอุทธรณ์ภายในกำหนด (เช่น 7 วัน)
- DP-3: Ban/Blacklist ถาวร ต้องผ่านการยืนยันของมนุษย์ (Admin) — ห้าม auto-ban ถาวรล้วน
- DP-4: ผลอุทธรณ์ทุกกรณี log + แจ้งผู้ใช้

---

## 7. Traceability — Requirement <-> ความเสี่ยงกฎหมายที่ลดได้

| Requirement | ความเสี่ยงกฎหมายที่ลด | Legal Ref |
|---|---|---|
| FR-18, FR-22, FR-27, FR-30, OS-1, OS-2, OS-5 | พ.ร.บ.ระบบการชำระเงิน 2560 (ถือเงิน/escrow ไม่มีใบอนุญาต); ค่าสมาชิก/ค่าโปรโมตเป็นรายได้แยกขาด ไม่ใช่ % commission | #1 |
| FR-28, FR-29 | **e-money / เงินอิเล็กทรอนิกส์** — เครดิต non-cashable, ถอน/แลก/โอนไม่ได้ ใช้เฉพาะในแพลตฟอร์ม (เลี่ยง พ.ร.บ.ระบบการชำระเงิน 2560) | #1, #10 |
| FR-28 (single-level) | **แชร์ลูกโซ่ / ขายตรง** — referral ชั้นเดียวเท่านั้น ห้ามจ่ายหลายทอด (พ.ร.บ.ขายตรงและตลาดแบบตรง) | #9 |
| FR-27 (เปิดเผยราคา/วันหมด trial + แจ้งเตือน 14/3/1), **FR-32 (notification log + precondition auto-renew)** | **คุ้มครองผู้บริโภค** — เลี่ยงเก็บเงินเงียบ/auto-charge โดยไม่แจ้ง + **พิสูจน์ได้ว่าแจ้งก่อนตัดเงิน** (พ.ร.บ.คุ้มครองผู้บริโภค / สคบ.) | #8 |
| FR-28 (anti-abuse), FR-29 (ledger), FR-31 (config history) | ความโปร่งใส/ตรวจสอบย้อนหลัง/กันฉ้อโกงเครดิต | #6, #8 |
| **FR-33 (idempotency+atomicity เครดิต)** | integrity เครดิต/กัน double-credit-double-debit → ledger โปร่งใส ตรวจสอบ AML/ร้องเรียนได้ | #6 |
| **FR-34 (ข้อมูลความเห็นผู้ประเมิน + word-blocklist)** | ทำ FR-07/IS-8 ได้ระดับ data + คงข้อห้าม "รับประกัน/การันตี/ของแท้ 100%" ลดความรับผิดจากการรับประกันความแท้ | #2 |
| **FR-35 (config versioning + price snapshot)** | **คุ้มครองผู้บริโภค** — เปลี่ยนราคาไม่ย้อนหลังรอบที่ชำระแล้ว + ตรวจย้อนหลังได้ว่าใครชำระราคาเท่าไรตอนไหน | #8 |
| **FR-36 (PendingBanReview), DP-3** | due process — ห้าม auto-ban ถาวรล้วน (human-in-the-loop ก่อน ban) | #6 |
| **FR-37, NFR-A4 (append-only ระดับ DB)** | บังคับ immutable ของ Audit/Consent/Credit ที่ฐานข้อมูล → AML/due process/ตรวจสอบย้อนหลังกันแก้-ลบ log แม้เข้าถึง DB ตรง | #6, #7 |
| FR-07, FR-06 (disclaimer), OS-3 | ความรับผิดจากการ "รับประกันความแท้" / โฆษณาเกินจริง | #2 |
| FR-17, NFR-P4 | หมิ่นประมาท / เปิดเผยข้อมูลเกินจำเป็น (Blacklist ประจาน) | #3 |
| FR-02, FR-03, NFR-P2 | PDPA — เก็บภาพบัตร/บัญชีดิบโดยไม่จำเป็น | #4 |
| OS-4 | ระดมทุน/รับฝากเงินล่วงหน้า (Pre-Order) | #5 |
| FR-11 | ปั่นราคา/ฉ้อโกงผู้บริโภค (shill bidding) | #6 |
| FR-15, FR-25, DP-1..4 | ละเมิดสิทธิผู้ใช้จากการลงโทษโดยไม่มี due process | #6 |
| FR-24, FR-26, NFR-A1 | ตรวจสอบย้อนหลัง / AML / ความรับผิดทางกฎหมาย | #6, #7 |
| FR-01, FR-04, NFR-P1, NFR-P3 | PDPA — consent / สิทธิเจ้าของข้อมูล | #7 |

---

## 8. รายการที่ "ต้องให้ทนายมีใบอนุญาตตรวจก่อน" (Legal Sign-off Checklist)

1. **ถ้อยคำ disclaimer** ทุกจุด (listing, ความเห็นผู้ประเมิน, หน้าโอนตรง, ToS) — ยืนยันว่าแพลตฟอร์มเป็นตัวกลาง ไม่ใช่ผู้รับประกัน/ตัวกลางการเงิน
2. **No-touch payment design** — ยืนยันว่าโครงสร้าง FR-18/FR-22 ไม่เข้าข่ายต้องขอใบอนุญาตตาม พ.ร.บ.ระบบการชำระเงิน 2560
3. **ถ้อยคำ Blacklist/warn-on-deal (FR-17)** — ตรวจว่าไม่เข้าข่ายหมิ่นประมาท และชอบด้วย PDPA (ฐานประโยชน์โดยชอบด้วยกฎหมาย)
4. **e-KYC/NDID + fallback retention (FR-02/03)** — ตรวจความสอดคล้อง PDPA, ระยะเก็บ, masking, การลบ
5. **Consent flow + นโยบายความเป็นส่วนตัว (FR-01/04)** — เวอร์ชัน, ภาษา, สิทธิเจ้าของข้อมูล
6. **กระบวนการ due process/อุทธรณ์ (FR-15, DP-1..4)** — ความเป็นธรรม, ระยะเวลา, การแจ้ง
7. **Audit log retention & AML reporting (FR-24/26)** — ระยะเวลาเก็บตามกฎหมายบัญชี/AML
8. **Terms of Service & ข้อจำกัดความรับผิด** — ครอบคลุมการเป็นตัวกลาง, ความเสี่ยงสินค้าปลอม, การโอนตรง
9. **สถานะ "ผู้ประเมินอิสระ"** — สัญญา/ความรับผิด แยกจากแพลตฟอร์ม
10. **Referral single-level + Credit non-cashable (FR-28/29)** *(ใหม่ v0.2)* — ตรวจว่าโครงสร้าง referral **เป็นชั้นเดียวจริง ไม่เข้าข่ายแชร์ลูกโซ่/ขายตรง** (พ.ร.บ.ขายตรงฯ) และเครดิต **ถอน/แลกเงิน/โอนไม่ได้ ใช้เฉพาะในแพลตฟอร์ม ไม่เข้าข่าย e-money** (พ.ร.บ.ระบบการชำระเงิน 2560); ตรวจเงื่อนไขวันหมดอายุ + anti-abuse
11. **การเปิดเผยราคา + Free Trial + Auto-renew (FR-27)** *(ใหม่ v0.2)* — ตรวจว่า **เปิดเผยราคา/วันหมด trial ตั้งแต่สมัคร**, แจ้งเตือนก่อนตัด/ต่ออายุ (14/3/1 วัน), ปิด auto-renew ได้, ไม่เก็บเงินเงียบ (พ.ร.บ.คุ้มครองผู้บริโภค / ประกาศ สคบ. เรื่องสัญญา/บริการต่ออายุอัตโนมัติ); ตรวจถ้อยคำ ToS เรื่องค่าสมาชิก/เครดิต/โปรโมต
12. **การแยกขาด no-touch ของค่าสมาชิก/เครดิต/ค่าโปรโมต (FR-22/27/30)** — ยืนยันว่าไม่ผูกกับมูลค่าธุรกรรมซื้อขาย (ไม่ใช่ % commission)
13. **หลักฐานการแจ้งเตือนก่อน auto-renew (FR-27/FR-32)** *(ใหม่ v0.3)* — ตรวจว่า Notification log พิสูจน์ได้ว่า**แจ้งล่วงหน้าครบ (14/3/1 วัน) ก่อนตัดเงิน**, กันส่งซ้ำ, และ auto-renew ถูกบล็อกถ้าหลักฐานไม่ครบ (พ.ร.บ.คุ้มครองผู้บริโภค / สคบ. เรื่องบริการต่ออายุอัตโนมัติ)
14. **ประวัติเวอร์ชัน config + price snapshot ไม่ย้อนหลัง (FR-31/FR-35)** *(ใหม่ v0.3)* — ตรวจว่าการเปลี่ยนราคา/ค่าสมาชิก/แพ็กเกจ**ไม่กระทบรอบที่ชำระไปแล้ว** และตรวจย้อนหลังได้ว่าใครชำระราคาเท่าไรตอนไหน (คุ้มครองผู้บริโภค + ตรวจสอบ)
15. **ถ้อยคำ + word-blocklist ของความเห็นผู้ประเมินอิสระ (FR-07/FR-34)** *(ใหม่ v0.3)* — ตรวจว่าข้อมูลความเห็นเก็บ disclaimer เวอร์ชัน + ระบุผู้ประเมิน และระบบ**ปฏิเสธคำว่า "รับประกัน/การันตี/ของแท้ 100%"** ในระดับ data/validation (ลดความรับผิดการรับประกันความแท้)
16. **Append-only/immutability ระดับฐานข้อมูล (FR-37/NFR-A4)** *(ใหม่ v0.3)* — ตรวจว่า AuditLogs/ConsentRecords/CreditTransactions ถูกบังคับ immutable ที่ DB (trigger/deny/temporal) เพื่อรองรับ AML/due process แม้มีการเข้าถึง DB ตรง

> เอกสารนี้เป็น DRAFT ของ sa-analyst — **ห้ามเริ่ม dev ก่อนผ่าน sign-off ข้อ 1–16 โดยทนายมีใบอนุญาต**
