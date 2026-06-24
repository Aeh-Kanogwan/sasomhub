using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldGapsM1M2M3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnonymizedAtUtc",
                schema: "dbo",
                table: "Users",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientHash",
                schema: "dbo",
                table: "NotificationDeliveryLog",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMockData",
                schema: "dbo",
                table: "KycSensitiveData",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ConfigVersions",
                columns: new[] { "ConfigVersionId", "ConfigKey", "CreatedAtUtc", "CreatedByUserId", "EffectiveFromUtc", "Note", "Value" },
                values: new object[] { 8L, "Disclaimer.AppraisalVersion", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: initial appraisal disclaimer version (FR-07, LEGAL #2)", "v1" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "ConfigVersions",
                keyColumn: "ConfigVersionId",
                keyValue: 8L);

            migrationBuilder.DropColumn(
                name: "AnonymizedAtUtc",
                schema: "dbo",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecipientHash",
                schema: "dbo",
                table: "NotificationDeliveryLog");

            migrationBuilder.DropColumn(
                name: "IsMockData",
                schema: "dbo",
                table: "KycSensitiveData");
        }
    }
}
