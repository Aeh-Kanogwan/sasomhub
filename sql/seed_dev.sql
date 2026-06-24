-- seed_dev.sql — DEV-ONLY seed data (Option A: schema.sql is source of truth)
--
-- NOTE: this file no longer stamps __EFMigrationsHistory. Migration stamping is owned by
-- sql/deploy.ps1, which stamps EVERY migration found on disk (idempotent) so it never goes
-- stale as new migrations are added. Run deploy.ps1 for the full one-command deploy; this
-- file is kept for ad-hoc re-seeding of an already-deployed dev DB.
SET NOCOUNT ON;

-- (a) BlacklistReasonCode 7 = COMPLETED_DEAL (+5 trust reward on completed deal; FR-13..16 / TradeService)
IF NOT EXISTS (SELECT 1 FROM dbo.BlacklistReasonCodes WHERE ReasonCodeId = 7)
  INSERT INTO dbo.BlacklistReasonCodes (ReasonCodeId, Code, DisplayName, Severity, DefaultScorePenalty, IsActive)
  VALUES (7, 'COMPLETED_DEAL', N'Completed deal', 1, 5, 1);

-- (b) ConfigVersions baseline (prices + referral rewards + expiry; resolved by ConfigVersionResolver)
IF NOT EXISTS (SELECT 1 FROM dbo.ConfigVersions WHERE ConfigKey = 'MembershipTier.Normal.AnnualPriceTHB')
  INSERT INTO dbo.ConfigVersions (ConfigKey, Value, EffectiveFromUtc, Note) VALUES
   ('MembershipTier.Normal.AnnualPriceTHB',   '1000', '2020-01-01T00:00:00', N'seed baseline'),
   ('MembershipTier.Verified.AnnualPriceTHB', '1500', '2020-01-01T00:00:00', N'seed baseline'),
   ('MembershipTier.Premium.AnnualPriceTHB',  '2000', '2020-01-01T00:00:00', N'seed baseline'),
   ('Membership.TrialMonths',                 '3',    '2020-01-01T00:00:00', N'seed baseline'),
   ('Referral.RewardCreditToReferrer',        '100',  '2020-01-01T00:00:00', N'seed baseline (confirm with business/legal)'),
   ('Referral.RewardCreditToReferred',        '50',   '2020-01-01T00:00:00', N'seed baseline (confirm with business/legal)'),
   ('Referral.CreditExpiryDays',              '180',  '2020-01-01T00:00:00', N'seed baseline');

-- (c) Flow B company bank account (where members transfer the membership fee). PLACEHOLDER values —
--     business must replace with the real company account before go-live.
IF NOT EXISTS (SELECT 1 FROM dbo.ConfigVersions WHERE ConfigKey = 'Company.BankName')
  INSERT INTO dbo.ConfigVersions (ConfigKey, Value, EffectiveFromUtc, Note) VALUES
   ('Company.BankName',        N'ธนาคารกสิกรไทย',          '2020-01-01T00:00:00', N'PLACEHOLDER — business to replace'),
   ('Company.BankAccountNo',   N'123-4-56789-0',           '2020-01-01T00:00:00', N'PLACEHOLDER — business to replace'),
   ('Company.BankAccountName', N'บริษัท เนออนวอลต์ จำกัด',  '2020-01-01T00:00:00', N'PLACEHOLDER — business to replace'),
   ('Company.PromptPayId',     N'0123456789',              '2020-01-01T00:00:00', N'PLACEHOLDER — business to replace');

SELECT 'reasoncodes' = COUNT(*) FROM dbo.BlacklistReasonCodes;
SELECT 'configversions' = COUNT(*) FROM dbo.ConfigVersions;
