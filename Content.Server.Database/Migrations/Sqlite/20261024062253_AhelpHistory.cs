using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AhelpHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ahelp_messages",
                columns: table => new
                {
                    ahelp_messages_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    round_id = table.Column<int>(type: "INTEGER", nullable: true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sender_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    sent_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    sender_is_admin = table.Column<bool>(type: "INTEGER", nullable: false),
                    admin_only = table.Column<bool>(type: "INTEGER", nullable: false),
                    play_sound = table.Column<bool>(type: "INTEGER", nullable: false),
                    from_webhook = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ahelp_messages", x => x.ahelp_messages_id);
                    table.ForeignKey(
                        name: "FK_ahelp_messages_round_round_id",
                        column: x => x.round_id,
                        principalTable: "round",
                        principalColumn: "round_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ahelp_messages_player_user_id_ahelp_messages_id",
                table: "ahelp_messages",
                columns: new[] { "player_user_id", "ahelp_messages_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ahelp_messages_round_id",
                table: "ahelp_messages",
                column: "round_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ahelp_messages");
        }
    }
}
