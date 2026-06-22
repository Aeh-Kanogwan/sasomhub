using System.ComponentModel.DataAnnotations;

namespace Marketplace.Web.Models.Account;

/// <summary>
/// FR-15 (due process DP-x): a user's appeal against a blacklist/penalty entry. We record the appeal
/// note on the BlacklistEntry and flip its AppealStatus to Requested for human review — we never
/// auto-resolve. No defamatory free text is exposed; the note is the user's own statement.
/// </summary>
public class AppealViewModel
{
    /// <summary>The BlacklistEntry the user is appealing (from ProfileViewModel.AppealableEntryId).</summary>
    [Required]
    public Guid BlacklistEntryId { get; set; }

    [Required(ErrorMessage = "กรุณาระบุเหตุผลในการอุทธรณ์"), StringLength(2000, MinimumLength = 10)]
    public string AppealNote { get; set; } = string.Empty;
}
