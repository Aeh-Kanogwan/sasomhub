using System.Linq;
using Marketplace.Application.Memberships;
using Marketplace.Application.Payments;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class PaymentSlipServiceTests
{
    private const byte PremiumTier = 3; // seeded 2000

    private static (MembershipService membership, PaymentSlipService slips) Build(MarketplaceDbContext db)
    {
        var membership = new MembershipService(db, new ConfigVersionResolver(db));
        var slips = new PaymentSlipService(db, membership, NullLogger<PaymentSlipService>.Instance);
        return (membership, slips);
    }

    /// <summary>Trial → Renew (issues invoice) → return (services, userId, invoiceId, membershipId).</summary>
    private static async Task<(MembershipService membership, PaymentSlipService slips, Guid userId, Guid invoiceId, Guid membershipId)>
        TrialThenRenewAsync(MarketplaceDbContext db)
    {
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var (membership, slips) = Build(db);
        var trial = await membership.StartTrialAsync(new StartTrialRequest(user.UserId, PremiumTier));
        await membership.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: false));
        var invoice = await db.FeeInvoices.FirstAsync(f => f.RelatedMembershipId == trial.Value!.MembershipId);
        return (membership, slips, user.UserId, invoice.FeeInvoiceId, trial.Value!.MembershipId);
    }

    [Fact]
    public async Task Submit_creates_pending_slip_for_own_issued_invoice()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, userId, invoiceId, _) = await TrialThenRenewAsync(db);

        var result = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/x.png", 2000m, DateTime.UtcNow, "ref-1"));

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentSlipStatus.Pending, result.Value!.Status);
        Assert.Single(db.PaymentSlips.Where(s => s.FeeInvoiceId == invoiceId));
    }

    [Fact]
    public async Task Submit_rejects_invoice_not_owned_by_user()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, _, invoiceId, _) = await TrialThenRenewAsync(db);
        var stranger = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        var result = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, stranger.UserId, "/uploads/slips/x.png", 2000m, DateTime.UtcNow, null));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Submit_returns_existing_pending_slip_instead_of_duplicating()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, userId, invoiceId, _) = await TrialThenRenewAsync(db);

        var first = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/a.png", 2000m, DateTime.UtcNow, null));
        var second = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/b.png", 2000m, DateTime.UtcNow, null));

        Assert.True(second.Succeeded);
        Assert.Equal(first.Value!.PaymentSlipId, second.Value!.PaymentSlipId); // same slip, no duplicate
        Assert.Single(db.PaymentSlips.Where(s => s.FeeInvoiceId == invoiceId));
    }

    [Fact]
    public async Task Approve_marks_invoice_paid_and_renews_membership_and_audits()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, userId, invoiceId, membershipId) = await TrialThenRenewAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var submit = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/x.png", 2000m, DateTime.UtcNow, null));

        var approve = await slips.ApproveSlipAsync(submit.Value!.PaymentSlipId, admin.UserId, "ok");

        Assert.True(approve.Succeeded);
        Assert.Equal(PaymentSlipStatus.Approved, approve.Value!.Status);

        var invoice = db.FeeInvoices.Single(f => f.FeeInvoiceId == invoiceId);
        Assert.Equal(FeeInvoiceStatus.Paid, invoice.Status);
        Assert.NotNull(invoice.PaidAtUtc);
        Assert.Equal($"SLIP:{submit.Value!.PaymentSlipId:N}", invoice.ExternalPaymentRef);

        var membership = db.Memberships.Single(m => m.MembershipId == membershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status); // renew applied on approval
        Assert.NotNull(membership.PaidThroughUtc);

        Assert.Contains(db.AuditLogs, a => a.Action == "PaymentSlip.Approve");
    }

    [Fact]
    public async Task Reject_keeps_invoice_issued_does_not_renew_and_audits()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, userId, invoiceId, membershipId) = await TrialThenRenewAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var submit = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/x.png", 2000m, DateTime.UtcNow, null));

        var reject = await slips.RejectSlipAsync(submit.Value!.PaymentSlipId, admin.UserId, "blurry");

        Assert.True(reject.Succeeded);
        Assert.Equal(PaymentSlipStatus.Rejected, reject.Value!.Status);

        var invoice = db.FeeInvoices.Single(f => f.FeeInvoiceId == invoiceId);
        Assert.Equal(FeeInvoiceStatus.Issued, invoice.Status); // still awaiting payment

        var membership = db.Memberships.Single(m => m.MembershipId == membershipId);
        Assert.Equal(MembershipStatus.Trial, membership.Status); // NOT renewed
        Assert.Null(membership.PaidThroughUtc);

        Assert.Contains(db.AuditLogs, a => a.Action == "PaymentSlip.Reject");
    }

    [Fact]
    public async Task Approve_is_idempotent_on_repeat()
    {
        await using var db = TestDb.NewContext();
        var (_, slips, userId, invoiceId, membershipId) = await TrialThenRenewAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var submit = await slips.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId, "/uploads/slips/x.png", 2000m, DateTime.UtcNow, null));

        await slips.ApproveSlipAsync(submit.Value!.PaymentSlipId, admin.UserId, "ok");
        var firstPaidThrough = db.Memberships.Single(m => m.MembershipId == membershipId).PaidThroughUtc;
        var second = await slips.ApproveSlipAsync(submit.Value!.PaymentSlipId, admin.UserId, "ok-again");

        Assert.True(second.Succeeded);
        var secondPaidThrough = db.Memberships.Single(m => m.MembershipId == membershipId).PaidThroughUtc;
        Assert.Equal(firstPaidThrough, secondPaidThrough); // no double extension
    }
}
