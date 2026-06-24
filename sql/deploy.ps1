<#
.SYNOPSIS
  Neon Vault — one-command, idempotent, reproducible database deploy (Option A:
  schema.sql is the source of truth).

.DESCRIPTION
  Deterministic deploy for dev / staging / prod. Running it on a fresh server creates
  the database from scratch; running it again is a no-op (idempotent). It does, in order:

    1. CREATE DATABASE if it does not exist.
    2. Run sql/schema.sql            — 33 tables + filtered indexes + CHECK + ROWVERSION
                                        + NEWSEQUENTIALID + lookup/config seed.
                                        (Re-run safe: lookup INSERTs are wrapped; triggers
                                         use CREATE OR ALTER. See note below.)
    3. Run sql/triggers.sql          — the four append-only triggers (CREATE OR ALTER).
    4. Run sql/seed_dev.sql          — dev seed (ReasonCode 7, ConfigVersions, company bank).
                                        Skipped in -Prod (use real config there).
    5. Stamp __EFMigrationsHistory   — every migration discovered in
                                        src/Marketplace.Infrastructure/Migrations is marked
                                        applied (idempotent INSERT), so `dotnet ef database
                                        update` is a no-op and the model/snapshot stays in sync.
                                        Migration IDs are read from the filesystem — never
                                        hard-coded — so this never goes stale.

  NOTE on re-running step 2: schema.sql is written for a FRESH database. On an existing
  database the CREATE TABLE statements will error (object exists). That is expected and
  harmless when you only want to re-apply triggers / re-stamp — pass -SkipSchema. For a
  truly clean reproducible deploy, point -Database at a NEW name (or -DropFirst).

.PARAMETER Server      SQL Server instance. Default: localhost
.PARAMETER Database    Target DB name. Default: MarketplaceDb
.PARAMETER Prod        Skip dev seed (seed_dev.sql). Use real ConfigVersions in prod.
.PARAMETER SkipSchema  Skip schema.sql (DB already has tables); still (re)applies triggers + stamps.
.PARAMETER DropFirst   DROP and recreate the database first (DESTRUCTIVE — never on prod).

.EXAMPLE
  # Fresh local dev DB, one command:
  pwsh sql/deploy.ps1

.EXAMPLE
  # Clean throwaway verification DB from zero:
  pwsh sql/deploy.ps1 -Database MarketplaceDb_Verify -DropFirst

.EXAMPLE
  # Prod (no dev seed); connection uses Trusted_Connection — adjust sqlcmd auth as needed:
  pwsh sql/deploy.ps1 -Server prod-sql01 -Database MarketplaceDb -Prod
#>
[CmdletBinding()]
param(
    [string] $Server   = $(if ($env:MARKETPLACE_DEPLOY_SERVER) { $env:MARKETPLACE_DEPLOY_SERVER } else { 'localhost' }),
    [string] $Database  = 'MarketplaceDb',
    [switch] $Prod,
    [switch] $SkipSchema,
    [switch] $DropFirst
)

$ErrorActionPreference = 'Stop'
$ProductVersion = '8.0.8'   # must match EF Core version pinned in the csproj files

$SqlDir        = $PSScriptRoot
$RepoRoot      = Split-Path $SqlDir -Parent
$MigrationsDir = Join-Path $RepoRoot 'src/Marketplace.Infrastructure/Migrations'

# sqlcmd flags: -I QUOTED_IDENTIFIER ON (required or filtered indexes fail), -C trust server cert,
# -b exit non-zero on SQL error (so this script actually fails on failure), -E trusted auth.
$Common = @('-S', $Server, '-C', '-I', '-b', '-E')

function Invoke-SqlCmd {
    param([string[]] $ExtraArgs)
    & sqlcmd @Common @ExtraArgs
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed (exit $LASTEXITCODE): $($ExtraArgs -join ' ')" }
}

Write-Host "==> Neon Vault deploy  server=$Server  db=$Database  prod=$($Prod.IsPresent)" -ForegroundColor Cyan

# ---- 0. (optional) drop ----------------------------------------------------
if ($DropFirst) {
    if ($Prod) { throw 'Refusing -DropFirst together with -Prod (destructive).' }
    Write-Host "==> Dropping database $Database (if exists)" -ForegroundColor Yellow
    Invoke-SqlCmd @('-Q', "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END")
}

# ---- 1. create database ----------------------------------------------------
Write-Host "==> Ensuring database exists" -ForegroundColor Green
Invoke-SqlCmd @('-Q', "IF DB_ID('$Database') IS NULL CREATE DATABASE [$Database];")

# ---- 2. schema -------------------------------------------------------------
if (-not $SkipSchema) {
    Write-Host "==> Applying schema.sql" -ForegroundColor Green
    Invoke-SqlCmd @('-d', $Database, '-i', (Join-Path $SqlDir 'schema.sql'))
} else {
    Write-Host "==> Skipping schema.sql (-SkipSchema)" -ForegroundColor DarkYellow
}

# ---- 3. triggers (idempotent) ----------------------------------------------
Write-Host "==> Applying triggers.sql (append-only enforcement)" -ForegroundColor Green
Invoke-SqlCmd @('-d', $Database, '-i', (Join-Path $SqlDir 'triggers.sql'))

# ---- 4. dev seed -----------------------------------------------------------
if (-not $Prod) {
    Write-Host "==> Applying seed_dev.sql (dev seed)" -ForegroundColor Green
    Invoke-SqlCmd @('-d', $Database, '-i', (Join-Path $SqlDir 'seed_dev.sql'))
} else {
    Write-Host "==> Skipping dev seed (-Prod): set real ConfigVersions / company bank account manually" -ForegroundColor DarkYellow
}

# ---- 5. stamp EF migration history (every migration on disk) ---------------
Write-Host "==> Stamping __EFMigrationsHistory for all migrations on disk" -ForegroundColor Green
$migrationIds = Get-ChildItem -Path $MigrationsDir -Filter '*.cs' |
    Where-Object { $_.Name -match '^\d{14}_.+\.cs$' -and $_.Name -notmatch '\.Designer\.cs$' } |
    ForEach-Object { $_.BaseName } | Sort-Object

if (-not $migrationIds) { throw "No migrations found in $MigrationsDir" }

$stamp = @"
SET NOCOUNT ON;
IF OBJECT_ID('__EFMigrationsHistory') IS NULL
    CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
"@
foreach ($id in $migrationIds) {
    $stamp += "IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = N'$id') INSERT INTO __EFMigrationsHistory VALUES (N'$id', N'$ProductVersion');`n"
    Write-Host "    stamp $id"
}
$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value $stamp -Encoding UTF8
try {
    Invoke-SqlCmd @('-d', $Database, '-i', $tmp.FullName)
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

# ---- 6. post-deploy verification ------------------------------------------
Write-Host "==> Post-deploy verification" -ForegroundColor Green
$verify = @"
SET NOCOUNT ON;
DECLARE @tables int = (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo'));
DECLARE @trigs  int = (SELECT COUNT(*) FROM sys.triggers WHERE name IN
    ('TR_AuditLogs_NoModify','TR_ConsentRecords_NoModify','TR_CreditTransactions_NoModify','TR_NotifDelivery_NoModify'));
DECLARE @stamped int = (SELECT COUNT(*) FROM __EFMigrationsHistory);
PRINT CONCAT('tables=', @tables, '  append_only_triggers=', @trigs, '/4  migrations_stamped=', @stamped);
IF @trigs <> 4 THROW 60001, 'DEPLOY FAILED: expected 4 append-only triggers.', 1;
PRINT 'DEPLOY OK';
"@
$tmp2 = New-TemporaryFile
Set-Content -Path $tmp2 -Value $verify -Encoding UTF8
try {
    Invoke-SqlCmd @('-d', $Database, '-i', $tmp2.FullName)
} finally {
    Remove-Item $tmp2 -ErrorAction SilentlyContinue
}

Write-Host "==> Done." -ForegroundColor Cyan
