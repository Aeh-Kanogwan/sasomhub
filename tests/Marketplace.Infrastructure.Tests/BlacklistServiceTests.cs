using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services.Reputation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M5 — FR-17 internal warn-on-deal blacklist + due-process ban/unban. Verifies the privacy invariant
/// (warnings expose code+severity ONLY, never the internal note), human-in-the-loop (propose flags
/// PendingBanReview, never an instant ban), admin confirm -> Banned + audit, reject restores, and
/// unban lifts the entry + restores the account. All actions are audited (FR-24).
/// </summary>
public class BlacklistServiceTests
{
    // Seeded BlacklistReasonCodes (SeedData): 5 = FAKE_ITEM (severity 4, "severe"),
    // 1 = LATE_SHIPMENT (severity 1, not severe).
    private const int ReasonFakeItemSevere = 5;
    private const int ReasonLateShipmentMild = 1;
    private const string FakeItemDisplay = "Counterfeit item";

    private static BlacklistService Build(MarketplaceDbContext db)
        => new(db, TestDb.Audit(db), NullLogger<BlacklistService>.Instance);

    [Fact]
    public async Task GetActiveWarnings_returns_code_and_severity_only_no_free_text()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        // Propose with a defamatory-style internal note, then confirm so it becomes an active warning.
        const string secretNote = "SECRET: this guy is a known scammer, ripped off 5 people";
        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, secretNote, admin.UserId));
        Assert.True(proposed.Succeeded);
        await svc.ReviewAsync(new ReviewBlacklistRequest(proposed.Value!.BlacklistEntryId, admin.UserId, Confirm: true, null));

        var warnings = await svc.GetActiveWarningsAsync(user.UserId);

        Assert.True(warnings.Succeeded);
        var w = Assert.Single(warnings.Value!);
        Assert.Equal(ReasonFakeItemSevere, w.ReasonCodeId);
        Assert.Equal(FakeItemDisplay, w.ReasonDisplayName);
        Assert.Equal((byte)4, w.Severity);
        // Privacy invariant (Legal #3): no field of the warning may leak the internal free-text note.
        var serialized = System.Text.Json.JsonSerializer.Serialize(w);
        Assert.DoesNotContain("scammer", serialized);
        Assert.DoesNotContain("SECRET", serialized);
    }

    [Fact]
    public async Task GetActiveWarnings_excludes_pending_and_rejected_entries()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        // A proposal that is never confirmed must NOT surface as a warn-on-deal warning.
        await svc.ProposeAsync(new ProposeBlacklistRequest(user.UserId, ReasonLateShipmentMild, "note", admin.UserId));

        var warnings = await svc.GetActiveWarningsAsync(user.UserId);

        Assert.True(warnings.Succeeded);
        Assert.Empty(warnings.Value!);
    }

    [Fact]
    public async Task Propose_severe_flags_PendingBanReview_not_Banned_and_audits()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, "review please", admin.UserId));

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(BlacklistReviewStatus.PendingReview), result.Value!.ReviewStatus);
        Assert.False(result.Value!.IsActive);

        var persistedUser = db.Users.Single(u => u.UserId == user.UserId);
        // Human-in-the-loop (DP-3): flagged for review, NOT banned yet.
        Assert.Equal(AccountStatus.PendingBanReview, persistedUser.AccountStatus);
        Assert.NotEqual(AccountStatus.Banned, persistedUser.AccountStatus);
        Assert.Contains(db.AuditLogs, a => a.Action == "Blacklist.Proposed");
    }

    [Fact]
    public async Task Propose_mild_reason_does_not_change_account_status()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        await svc.ProposeAsync(new ProposeBlacklistRequest(user.UserId, ReasonLateShipmentMild, null, admin.UserId));

        Assert.Equal(AccountStatus.Active, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);
    }

    [Fact]
    public async Task Propose_with_invalid_reason_code_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.ProposeAsync(new ProposeBlacklistRequest(user.UserId, 9999, null, admin.UserId));

        Assert.False(result.Succeeded);
        Assert.Empty(db.BlacklistEntries);
    }

    [Fact]
    public async Task Admin_confirm_bans_account_activates_entry_and_audits()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, "evidence", admin.UserId));
        var entryId = proposed.Value!.BlacklistEntryId;

        var confirm = await svc.ReviewAsync(
            new ReviewBlacklistRequest(entryId, admin.UserId, Confirm: true, "Confirmed after review."));

        Assert.True(confirm.Succeeded);
        Assert.Equal(nameof(BlacklistReviewStatus.Confirmed), confirm.Value!.ReviewStatus);
        Assert.True(confirm.Value!.IsActive);

        var entry = db.BlacklistEntries.Single(e => e.BlacklistEntryId == entryId);
        Assert.Equal(BlacklistReviewStatus.Confirmed, entry.ReviewStatus);
        Assert.True(entry.IsActive);
        Assert.Equal(admin.UserId, entry.ReviewedByUserId);
        Assert.Equal(AccountStatus.Banned, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);
        Assert.Contains(db.AuditLogs, a => a.Action == "Blacklist.Confirmed");
    }

    [Fact]
    public async Task Admin_reject_restores_account_and_does_not_ban()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, "evidence", admin.UserId));
        Assert.Equal(AccountStatus.PendingBanReview, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);

        var reject = await svc.ReviewAsync(
            new ReviewBlacklistRequest(proposed.Value!.BlacklistEntryId, admin.UserId, Confirm: false, "Insufficient evidence."));

        Assert.True(reject.Succeeded);
        Assert.Equal(nameof(BlacklistReviewStatus.Rejected), reject.Value!.ReviewStatus);
        Assert.False(reject.Value!.IsActive);
        Assert.Equal(AccountStatus.Active, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);
        Assert.Contains(db.AuditLogs, a => a.Action == "Blacklist.Rejected");
    }

    [Fact]
    public async Task Review_non_pending_entry_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, null, admin.UserId));
        var entryId = proposed.Value!.BlacklistEntryId;
        await svc.ReviewAsync(new ReviewBlacklistRequest(entryId, admin.UserId, Confirm: true, null));

        // A second review on an already-confirmed entry must fail (not pending).
        var again = await svc.ReviewAsync(new ReviewBlacklistRequest(entryId, admin.UserId, Confirm: false, null));

        Assert.False(again.Succeeded);
        Assert.Equal(BlacklistReviewStatus.Confirmed, db.BlacklistEntries.Single(e => e.BlacklistEntryId == entryId).ReviewStatus);
    }

    [Fact]
    public async Task Unban_lifts_entry_restores_account_and_audits()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, "evidence", admin.UserId));
        var entryId = proposed.Value!.BlacklistEntryId;
        await svc.ReviewAsync(new ReviewBlacklistRequest(entryId, admin.UserId, Confirm: true, null));
        Assert.Equal(AccountStatus.Banned, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);

        var unban = await svc.UnbanAsync(entryId, admin.UserId, "Appeal accepted.");

        Assert.True(unban.Succeeded);
        Assert.False(unban.Value!.IsActive);
        Assert.Equal(nameof(BlacklistReviewStatus.Overturned), unban.Value!.ReviewStatus);
        Assert.Equal(AccountStatus.Active, db.Users.Single(u => u.UserId == user.UserId).AccountStatus);
        Assert.Contains(db.AuditLogs, a => a.Action == "Blacklist.Lifted");

        // Lifted entry no longer surfaces as a warning.
        var warnings = await svc.GetActiveWarningsAsync(user.UserId);
        Assert.Empty(warnings.Value!);
    }

    [Fact]
    public async Task Unban_on_non_active_entry_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        // A pending (never-confirmed) entry cannot be "unbanned".
        var proposed = await svc.ProposeAsync(
            new ProposeBlacklistRequest(user.UserId, ReasonFakeItemSevere, null, admin.UserId));

        var unban = await svc.UnbanAsync(proposed.Value!.BlacklistEntryId, admin.UserId, null);

        Assert.False(unban.Succeeded);
    }
}
