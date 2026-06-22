using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionNotificationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notif_Type",
                schema: "dbo",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notif_Type",
                schema: "dbo",
                table: "Notifications",
                sql: "[Type] IN ('TrialExpiring','RenewalDue','RenewalCharged','MembershipExpired','PromotionExpiring','PenaltyIssued','AppealUpdate','AuctionWon','AuctionClosed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notif_Type",
                schema: "dbo",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notif_Type",
                schema: "dbo",
                table: "Notifications",
                sql: "[Type] IN ('TrialExpiring','RenewalDue','RenewalCharged','MembershipExpired','PromotionExpiring','PenaltyIssued','AppealUpdate')");
        }
    }
}
