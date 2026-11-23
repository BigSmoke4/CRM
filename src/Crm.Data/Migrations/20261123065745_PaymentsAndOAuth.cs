using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsAndOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthAuthorizeUrl",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientIdEncrypted",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientSecretEncrypted",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthProvider",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthScope",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthTokenUrl",
                table: "IntegrationConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SecretKeyEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishableKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WebhookSecretEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthAuthorizeUrl",
                table: "IntegrationConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthClientIdEncrypted",
                table: "IntegrationConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthClientSecretEncrypted",
                table: "IntegrationConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthProvider",
                table: "IntegrationConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthScope",
                table: "IntegrationConfigs");

            migrationBuilder.DropColumn(
                name: "OAuthTokenUrl",
                table: "IntegrationConfigs");
        }
    }
}
