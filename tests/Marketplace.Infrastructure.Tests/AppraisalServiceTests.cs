using Marketplace.Application.Catalog;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Marketplace.Infrastructure.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M6 — FR-07 / LEGAL #2 appraisal opinions (opinion only, never a warranty). Verifies disclaimer
/// version stamping (from ConfigVersions, with default fallback), forbidden warranty-wording rejection,
/// draft -> publish visibility, unpublish, and that every write emits an audit row.
/// </summary>
public class AppraisalServiceTests
{
    private static AppraisalService Build(MarketplaceDbContext db)
    {
        var resolver = new ConfigVersionResolver(db);
        var audit = TestDb.Audit(db);
        return new AppraisalService(db, resolver, audit, NullLogger<AppraisalService>.Instance);
    }

    /// <summary>Seed a product (with seller + category) and return its id.</summary>
    private static async Task<Guid> SeedProductAsync(MarketplaceDbContext db)
    {
        var seller = TestDb.AddUser(db);
        var category = new Category { Name = "การ์ดสะสม", Slug = $"cat-{Guid.NewGuid():N}" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            SellerId = seller.UserId,
            CategoryId = category.CategoryId,
            Title = "การ์ดทดสอบ",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.ProductId;
    }

    [Fact]
    public async Task Create_stamps_configured_disclaimer_version_and_audits()
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);
        TestDb.SetConfig(db, AppraisalService.DisclaimerConfigKey, "v2.1", DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ ก", "สภาพดี น่าจะออกปีนี้", Publish: false));

        Assert.True(result.Succeeded);
        Assert.Equal("v2.1", result.Value!.DisclaimerVersion);
        Assert.False(result.Value.IsPublished);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AppraisalOpinion.Created"));
    }

    [Fact]
    public async Task Create_uses_default_disclaimer_when_unset()
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);

        // The baseline seed now ships a Disclaimer.AppraisalVersion row; remove it so this test genuinely
        // exercises the "key unset" in-code fallback path (AppraisalService.DefaultDisclaimerVersion).
        var seeded = await db.ConfigVersions
            .Where(c => c.ConfigKey == AppraisalService.DisclaimerConfigKey)
            .ToListAsync();
        db.ConfigVersions.RemoveRange(seeded);
        await db.SaveChangesAsync();

        var svc = Build(db);

        var result = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ ข", "ความเห็นทั่วไป", Publish: false));

        Assert.True(result.Succeeded);
        Assert.Equal(AppraisalService.DefaultDisclaimerVersion, result.Value!.DisclaimerVersion);
    }

    [Theory]
    [InlineData("การ์ดใบนี้รับประกันของแท้")]
    [InlineData("การันตีความแท้")]
    [InlineData("เป็นของแท้ 100% แน่นอน")]
    [InlineData("we guarantee this card")]
    [InlineData("this is 100% AUTHENTIC")]
    public async Task Create_rejects_forbidden_warranty_wording(string opinion)
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);
        var svc = Build(db);

        var result = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ", opinion, Publish: false));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Empty(db.AppraisalOpinions);
    }

    [Fact]
    public async Task Create_unknown_product_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var svc = Build(db);

        var result = await svc.CreateAsync(
            new CreateAppraisalRequest(Guid.NewGuid(), null, "ผู้เชี่ยวชาญ", "ความเห็น", Publish: false));

        Assert.False(result.Succeeded);
        Assert.Empty(db.AppraisalOpinions);
    }

    [Fact]
    public async Task Draft_is_hidden_from_published_list_until_published()
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);
        var svc = Build(db);

        var created = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ", "ความเห็น", Publish: false));
        Assert.True(created.Succeeded);

        // Public list (published only) is empty; the admin view (include unpublished) sees the draft.
        var publicList = await svc.GetForProductAsync(productId, includeUnpublished: false);
        Assert.Empty(publicList.Value!);
        var adminList = await svc.GetForProductAsync(productId, includeUnpublished: true);
        Assert.Single(adminList.Value!);

        // Publish -> now visible publicly, and the publish is audited.
        var published = await svc.PublishAsync(created.Value!.AppraisalOpinionId, Guid.NewGuid());
        Assert.True(published.Succeeded);
        Assert.True(published.Value!.IsPublished);

        publicList = await svc.GetForProductAsync(productId, includeUnpublished: false);
        Assert.Single(publicList.Value!);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AppraisalOpinion.Published"));
    }

    [Fact]
    public async Task Publish_immediately_then_unpublish_removes_from_public_list()
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);
        var svc = Build(db);

        var created = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ", "ความเห็น", Publish: true));
        Assert.True(created.Value!.IsPublished);
        Assert.Single((await svc.GetForProductAsync(productId)).Value!);

        var unpub = await svc.UnpublishAsync(created.Value.AppraisalOpinionId, Guid.NewGuid());
        Assert.True(unpub.Succeeded);
        Assert.Empty((await svc.GetForProductAsync(productId)).Value!);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "AppraisalOpinion.Unpublished"));
    }

    [Fact]
    public async Task Publish_already_published_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var productId = await SeedProductAsync(db);
        var svc = Build(db);

        var created = await svc.CreateAsync(
            new CreateAppraisalRequest(productId, null, "ผู้เชี่ยวชาญ", "ความเห็น", Publish: true));

        var again = await svc.PublishAsync(created.Value!.AppraisalOpinionId, Guid.NewGuid());
        Assert.False(again.Succeeded);
    }
}
