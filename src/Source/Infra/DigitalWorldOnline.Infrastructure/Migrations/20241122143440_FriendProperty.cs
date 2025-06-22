using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FriendProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*migrationBuilder.CreateIndex(
                name: "IX_Friend_FriendId",
                schema: "Character",
                table: "Friend",
                column: "FriendId");

            migrationBuilder.AddForeignKey(
                name: "FK_Friend_Tamer_FriendId",
                schema: "Character",
                table: "Friend",
                column: "FriendId",
                principalSchema: "Character",
                principalTable: "Tamer",
                principalColumn: "Id");*/

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Friend_FriendId' AND object_id = OBJECT_ID('Character.Friend'))
                BEGIN
                    CREATE INDEX IX_Friend_FriendId ON Character.Friend (FriendId);
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Friend_Tamer_FriendId' AND parent_object_id = OBJECT_ID('Character.Friend'))
                BEGIN
                    ALTER TABLE Character.Friend
                    ADD CONSTRAINT FK_Friend_Tamer_FriendId FOREIGN KEY (FriendId)
                    REFERENCES Character.Tamer (Id);
                END;
            ");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Friend_Tamer_FriendId",
                schema: "Character",
                table: "Friend");

            migrationBuilder.DropIndex(
                name: "IX_Friend_FriendId",
                schema: "Character",
                table: "Friend");
        }
    }
}
