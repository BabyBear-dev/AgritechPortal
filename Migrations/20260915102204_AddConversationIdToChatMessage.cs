using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgritechPortal.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationIdToChatMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversationId",
                table: "ChatMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_OperatorEmail_ConversationId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "OperatorEmail", "ConversationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_OperatorEmail_ConversationId_CreatedAt",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "ChatMessages");
        }
    }
}
