using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSlips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "PendingUpgradeTierId",
                schema: "dbo",
                table: "Memberships",
                type: "tinyint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentSlips",
                schema: "dbo",
                columns: table => new
                {
                    PaymentSlipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    FeeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlipImageUrl = table.Column<string>(type: "nvarchar(512)", nullable: false),
                    AmountClaimed = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TransferredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    BankRefNote = table.Column<string>(type: "nvarchar(200)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Pending"),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(400)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSlips", x => x.PaymentSlipId);
                    table.CheckConstraint("CK_Slip_Amount", "[AmountClaimed] >= 0");
                    table.CheckConstraint("CK_Slip_Status", "[Status] IN ('Pending','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_Slip_Invoice",
                        column: x => x.FeeInvoiceId,
                        principalSchema: "dbo",
                        principalTable: "FeeInvoices",
                        principalColumn: "FeeInvoiceId");
                    table.ForeignKey(
                        name: "FK_Slip_ReviewedBy",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Slip_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSlips_ReviewedByUserId",
                schema: "dbo",
                table: "PaymentSlips",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSlips_UserId",
                schema: "dbo",
                table: "PaymentSlips",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Slip_Invoice",
                schema: "dbo",
                table: "PaymentSlips",
                column: "FeeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Slip_Status_Submitted",
                schema: "dbo",
                table: "PaymentSlips",
                columns: new[] { "Status", "SubmittedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentSlips",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "PendingUpgradeTierId",
                schema: "dbo",
                table: "Memberships");
        }
    }
}
