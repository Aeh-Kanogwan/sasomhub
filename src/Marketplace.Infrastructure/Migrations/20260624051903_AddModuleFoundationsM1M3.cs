using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleFoundationsM1M3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataSubjectRequests",
                schema: "dbo",
                columns: table => new
                {
                    DataSubjectRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    RequestType = table.Column<string>(type: "varchar(20)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Pending"),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    HandledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultArtifactPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DueByUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSubjectRequests", x => x.DataSubjectRequestId);
                    table.CheckConstraint("CK_Dsar_Status", "[Status] IN ('Pending','InProgress','Completed','Rejected')");
                    table.CheckConstraint("CK_Dsar_Type", "[RequestType] IN ('Export','Erasure','Access','Rectify','WithdrawConsent')");
                    table.ForeignKey(
                        name: "FK_Dsar_HandledBy",
                        column: x => x.HandledByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Dsar_Requester",
                        column: x => x.RequestedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryLog",
                schema: "dbo",
                columns: table => new
                {
                    NotificationDeliveryLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "varchar(200)", nullable: true),
                    Channel = table.Column<string>(type: "varchar(20)", nullable: false),
                    RecipientMasked = table.Column<string>(type: "varchar(120)", nullable: false),
                    TemplateKey = table.Column<string>(type: "varchar(80)", nullable: false),
                    TemplateVersion = table.Column<string>(type: "varchar(20)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Queued"),
                    ErrorDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PayloadSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RetentionExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryLog", x => x.NotificationDeliveryLogId);
                    table.CheckConstraint("CK_NotifDelivery_Channel", "[Channel] IN ('Email','Sms')");
                    table.CheckConstraint("CK_NotifDelivery_Status", "[Status] IN ('Queued','Sent','Failed','Retrying')");
                    table.ForeignKey(
                        name: "FK_NotifDelivery_Notification",
                        column: x => x.NotificationId,
                        principalSchema: "dbo",
                        principalTable: "Notifications",
                        principalColumn: "NotificationId");
                    table.ForeignKey(
                        name: "FK_NotifDelivery_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "KycStatuses",
                columns: new[] { "KycStatusId", "Code", "DisplayName" },
                values: new object[] { (byte)5, "INITIATED", "Initiated (awaiting provider)" });

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_HandledByUserId",
                schema: "dbo",
                table: "DataSubjectRequests",
                column: "HandledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Dsar_Requester",
                schema: "dbo",
                table: "DataSubjectRequests",
                columns: new[] { "RequestedByUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Dsar_Status_Due",
                schema: "dbo",
                table: "DataSubjectRequests",
                columns: new[] { "Status", "DueByUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotifDelivery_Correlation",
                schema: "dbo",
                table: "NotificationDeliveryLog",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotifDelivery_Retention",
                schema: "dbo",
                table: "NotificationDeliveryLog",
                column: "RetentionExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NotifDelivery_User",
                schema: "dbo",
                table: "NotificationDeliveryLog",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryLog_NotificationId",
                schema: "dbo",
                table: "NotificationDeliveryLog",
                column: "NotificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataSubjectRequests",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "NotificationDeliveryLog",
                schema: "dbo");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "KycStatuses",
                keyColumn: "KycStatusId",
                keyValue: (byte)5);
        }
    }
}
