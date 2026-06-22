using System.ComponentModel.DataAnnotations;

namespace Marketplace.Web.Models.Auctions;

/// <summary>
/// FR-10: a bid submission. Controller maps this to Application.Auctions.PlaceBidRequest and calls
/// IAuctionService.PlaceBidAsync. IP/device hashes for anti-shill (FR-11) are computed server-side
/// from the request (not posted by the client).
/// </summary>
public class PlaceBidViewModel
{
    [Required]
    public Guid AuctionId { get; set; }

    [Range(0, 999_999_999)]
    public decimal Amount { get; set; }
}
