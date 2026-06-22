# Prototype — Marketplace ของสะสม (Clickable HTML Mockup)

> ⚠️ **นี่คือ static mockup เท่านั้น** — HTML/CSS ล้วน ไม่ต่อ API/ฐานข้อมูล ใช้สำหรับสาธิต user flow และ UI เพื่อรีวิวกับทีม/ทนาย ก่อนพัฒนาจริง (ตาม SRS_MVP v0.3 ที่ยังต้องผ่าน legal sign-off)

## วิธีเปิด
เปิดไฟล์ **`index.html`** ด้วยเบราว์เซอร์ (ดับเบิลคลิกได้เลย) แล้วคลิกลิงก์/ปุ่มเพื่อข้ามหน้าได้จริง ทุกหน้าใช้ navbar/footer ร่วมกัน และโหลด `styles.css` ตัวเดียว

## รายการไฟล์
| ไฟล์ | หน้าจอ |
|---|---|
| `index.html` | หน้าแรก / ค้นหา / เรียกดูประกาศ (Guest) |
| `register.html` | สมัครสมาชิก (referral + consent PDPA + เปิดเผย Free Trial) |
| `login.html` | เข้าสู่ระบบ |
| `listing-detail.html` | รายละเอียดสินค้า + Trust Score + ความเห็นผู้ประเมินอิสระ |
| `auction.html` | หน้าประมูล (ราคาปัจจุบัน, นับถอยหลัง, ประวัติ bid) |
| `membership.html` | แพ็กเกจสมาชิก 3 ระดับ + auto-renew |
| `create-listing.html` | ลงประกาศขาย + โปรโมตด้วยเครดิต |
| `credits.html` | กระเป๋าเครดิต + ledger |
| `transfer.html` | โอนตรง (no-touch payment) |
| `profile.html` | โปรไฟล์ / Trust Score / สถานะสมาชิก |
| `styles.css` | Design system (ใช้ร่วมทุกหน้า) |

## แผนผังการลิงก์หน้าจอ (Sitemap)
```
index.html ──┬─ register.html ── profile.html
             ├─ login.html ───── profile.html
             ├─ listing-detail.html ── transfer.html ── profile.html
             ├─ auction.html ───────── (ต้องสมาชิก active เพื่อ bid)
             ├─ membership.html ────── transfer.html
             ├─ create-listing.html ── listing-detail.html / credits.html
             ├─ credits.html ───────── membership.html / create-listing.html
             └─ profile.html ───────── membership.html / credits.html

navbar/footer (ทุกหน้า): หน้าแรก · ประมูล · แพ็กเกจ · เครดิต · ลงประกาศ · เข้าสู่ระบบ/สมัคร
```

## Map หน้าจอ ↔ Functional Requirement (SRS_MVP v0.3)
| หน้า | FR ที่ครอบคลุม |
|---|---|
| index | FR-08 (Guest ค้นหา/ดู), FR-27 (trial banner), FR-30 (Featured) |
| register | FR-01 (สมัคร + consent PDPA), FR-28 (referral single-level), FR-27 (เปิดเผยราคา+trial) |
| login | NFR-S1 (auth) |
| listing-detail | FR-06 (disclaimer ตัวกลาง), FR-07/FR-34 (ความเห็นผู้ประเมิน + ห้ามคำรับประกัน), FR-13/16 (Trust Score) |
| auction | FR-09/10/11/12 (ตั้ง/เสนอ/anti-shill/ปิดประมูล), gate สมาชิก active |
| membership | FR-27 (3 tier 1000/1500/2000 + trial + auto-renew), FR-05 (อัพเกรด), FR-32 (แจ้ง 14/3/1), FR-35 (price snapshot) |
| create-listing | FR-06 (active gate + disclaimer + KYC threshold), FR-30/23 (โปรโมตด้วยเครดิต), FR-07 (ขอความเห็น) |
| credits | FR-28/29 (referral + ledger append-only + non-cashable), FR-30 (ใช้เครดิต) |
| transfer | FR-18 (no-touch: บัญชีผู้ขาย + "ไม่รับ/ไม่ถือเงิน"), FR-19 (ยืนยันรับของ), FR-17 (warn-on-deal) |
| profile | FR-16 (Trust history), FR-27 (สถานะ membership), FR-04 (PDPA), FR-36 (account state/due process), FR-17 (warn demo) |

## จุดที่ดีไซน์สะท้อนข้อจำกัดกฎหมาย
- **No-touch payment (Legal #1):** `transfer.html` แสดงบัญชีผู้ขายให้โอนตรง + แถบเตือนเด่น "แพลตฟอร์มไม่รับ ไม่ถือ และไม่เป็นตัวกลางการเงิน" ไม่มี wallet/escrow
- **ไม่รับประกันความแท้ (Legal #2):** disclaimer ในทุกหน้าที่เกี่ยวข้อง + ความเห็นผู้ประเมินอิสระระบุชัดว่า "ไม่ใช่การรับประกันของแพลตฟอร์ม" — เลี่ยงคำว่า รับประกัน/การันตี/ของแท้ 100%
- **คุ้มครองผู้บริโภค (Legal #8):** เปิดเผยราคา + วันหมด Free Trial ตั้งแต่หน้าสมัคร, ระบุการแจ้งเตือน 14/3/1 วัน, ไม่ตัดเงินถ้าไม่เปิด auto-renew
- **เครดิต non-cashable + referral ชั้นเดียว (Legal #9/#10):** หมายเหตุย้ำในหน้า credits/register, ledger แบบ append-only
- **Blacklist เป็นระบบเตือนภายใน (Legal #3):** warn-on-deal แสดงเฉพาะคู่ดีล ใช้ถ้อยคำกลาง ไม่ประจานสาธารณะ
- **Due process (Legal #6):** หน้า profile ระบุสิทธิอุทธรณ์ + แบนถาวรต้องผ่านเจ้าหน้าที่ยืนยัน
- footer ทุกหน้ามี legal-note สรุปข้อจำกัดหลัก + ribbon บนสุดเตือนว่าเป็น mockup

## สิ่งที่แนะนำทำต่อ
1. แปลงเป็น component จริง — **Blazor** (เข้ากับ stack .NET 8) หรือ **React/Next.js** โดยยก design tokens จาก `styles.css` ไปเป็น theme/CSS variables
2. ทำ wireframe หน้าฝั่ง **Admin** (จัดการ dispute/อุทธรณ์/config ราคา/audit log) ซึ่งยังไม่อยู่ใน prototype นี้
3. เพิ่ม flow e-KYC (NDID), หน้า dispute, และ state transition จริงของ membership/account
4. ให้ทีม Legal รีวิวถ้อยคำ disclaimer/consent ทุกจุดก่อน (ตาม Legal Sign-off Checklist ข้อ 1–16)
5. ทำ accessibility pass (focus order, ARIA, contrast) และทดสอบ responsive จริงบนมือถือ
