# Neon Vault — Prototype (Trading Card Marketplace)

Clickable HTML prototype (static, ไม่ต่อ API) ของ marketplace ขายการ์ดสะสม
ออกแบบตาม design system **"Neon Vault"** (Dark-Mode Glassmorphism + นีออน) เพื่อกลุ่ม Gen Y/Z

> ⚠️ เป็น **static mockup** เพื่อสาธิต flow/หน้าตาเท่านั้น — ปุ่ม submit จะ redirect ข้ามหน้าเฉย ๆ ไม่มีการเรียก API จริง

## วิธีเปิด
ดับเบิลคลิก `index.html` หรือสั่ง (ในช่องแชตขึ้นต้นด้วย `!`):
```
! start C:\Users\Kanogwan_L\MarketplaceProject\prototype-neonvault\index.html
```
ทุกหน้าลิงก์หากันผ่าน navbar/footer + ปุ่มเฉพาะหน้า คลิกสำรวจได้ทั้งหมด

## Sitemap (11 หน้า + styles.css)
```
index (หน้าแรก + market ticker + แกลเลอรีการ์ด)
 ├─ explore ............ ค้นหา/ฟิลเตอร์การ์ด (rarity/เกรด/ราคา/ชุด)
 │   └─ listing-detail . การ์ดเดี่ยว (refraction, เกรด PSA, ซีเรียล, ประวัติราคา)
 │        ├─ auction ... ประมูลสด (countdown, bid history)
 │        └─ transfer .. โอนตรง no-touch (บัญชีผู้ขาย + เตือน)
 ├─ register / login ... สมัคร (referral + consent PDPA) / เข้าสู่ระบบ
 ├─ membership ......... แพ็กเกจ 1,000 / 1,500 / 2,000 บาท/ปี + ฟรี 3 เดือน
 ├─ create-listing ..... ลงขายการ์ด + โปรโมตด้วยเครดิต
 ├─ credits ............ กระเป๋าเครดิต + ledger
 └─ profile ............ โปรไฟล์นักสะสม (XP/level, Trust Score, คอลเลกชัน)
```

## Map หน้า ↔ FR (จาก SRS_MVP v0.3)
| หน้า | FR ที่เกี่ยวข้อง |
|---|---|
| index / explore | FR-08 (ค้นหา/ดู Active — Guest ได้) |
| listing-detail | FR-06, FR-07 (ความเห็นผู้ประเมิน + disclaimer) |
| auction | FR-09, FR-10, FR-11 (สมาชิก active เท่านั้น) |
| register | FR-01 (consent PDPA), FR-28 (referral) |
| membership | FR-05, FR-22, FR-27 (trial 3 เดือน + ต่ออายุ) |
| create-listing | FR-06 (disclaimer), FR-30 (โปรโมตด้วยเครดิต) |
| credits | FR-28, FR-29 (ledger), non-cashable |
| transfer | FR-18 (no-touch), FR-19 (ยืนยันรับของ) |
| profile | FR-16 (Trust history), FR-15/17 (อุทธรณ์/warn-on-deal), FR-27 (สถานะสมาชิก) |

## Design tokens ที่ใช้ (จาก DESIGN.md)
- **สี:** background `#131318`, primary ฟ้านีออน `#00f0ff`, secondary ม่วง `#9d05ff`, tertiary เขียวมะนาว `#bbea00` (ปุ่มซื้อ), rarity Common/Rare/Epic/Legendary (เทา/ฟ้า/ม่วง/ทอง)
- **ฟอนต์:** Sora (หัวข้อ), Hanken Grotesk (เนื้อหา), JetBrains Mono (ราคา/ซีเรียล/เกรด PSA) — โหลดผ่าน Google Fonts
- **เอฟเฟกต์:** glassmorphism `backdrop-filter: blur(12px)`, hover glow + scale, **refraction overlay** บนการ์ด, **market ticker** เลื่อนราคา, **XP/level progress bar** ไล่เฉดฟ้า→ม่วง, gradient-text หัวข้อ
- spacing 8px scale, container 1280px, responsive (desktop grid / mobile 2-col)

## จุดที่ดีไซน์สะท้อนข้อจำกัดกฎหมาย
- **No-touch (Legal #1):** `transfer.html` โชว์บัญชีผู้ขายให้โอนตรง + แถบเตือนเด่น "แพลตฟอร์มไม่รับ ไม่ถือ ไม่เป็นตัวกลางการเงิน" — ไม่มี wallet/escrow
- **ไม่รับประกันความแท้ (Legal #2):** disclaimer + "ความเห็นประกอบจากผู้ประเมินอิสระ" เลี่ยงคำว่า รับประกัน/การันตี/ของแท้ 100%
- **คุ้มครองผู้บริโภค (Legal #8):** เปิดเผยราคา + วันหมด free trial ตั้งแต่หน้าสมัคร/แพ็กเกจ
- **เครดิต non-cashable + referral ชั้นเดียว:** หมายเหตุย้ำใน `credits.html` / `register.html`
- **Warn-on-deal + due process:** `profile.html` แสดงคำเตือนเฉพาะคู่ดีล + สิทธิอุทธรณ์

## งานแนะนำทำต่อ
1. แปลงเป็น **Blazor** (เข้ากับ stack .NET 8) หรือ React/Next.js — ยก design tokens จาก `styles.css` ไปเป็น theme
2. เพิ่มหน้าฝั่ง **Admin** (dispute/อุทธรณ์/ตั้งราคา/audit log)
3. เพิ่ม flow e-KYC (NDID), dispute, state transition จริงของ membership/account
4. ให้ทีม Legal รีวิวถ้อยคำ disclaimer/consent ตาม Sign-off Checklist (SRS §8 ข้อ 1–16)
5. Accessibility pass + ทดสอบ responsive บนมือถือจริง
