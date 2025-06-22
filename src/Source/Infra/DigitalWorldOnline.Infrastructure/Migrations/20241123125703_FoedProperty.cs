using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FoedProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*migrationBuilder.CreateIndex(
                name: "IX_Foe_FoeId",
                schema: "Character",
                table: "Foe",
                column: "FoeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Foe_Tamer_FoeId",
                schema: "Character",
                table: "Foe",
                column: "FoeId",
                principalSchema: "Character",
                principalTable: "Tamer",
                principalColumn: "Id");*/

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Foe_FoeId' AND object_id = OBJECT_ID('Character.Foe'))
                BEGIN
                    CREATE INDEX IX_Foe_FoeId ON Character.Foe (FoeId);
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Foe_Tamer_FoeId' AND parent_object_id = OBJECT_ID('Character.Foe'))
                BEGIN
                    ALTER TABLE Character.Foe
                    ADD CONSTRAINT FK_Foe_Tamer_FoeId FOREIGN KEY (FoeId)
                    REFERENCES Character.Tamer (Id);
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Foe_Tamer_FoeId",
                schema: "Character",
                table: "Foe");

            migrationBuilder.DropIndex(
                name: "IX_Foe_FoeId",
                schema: "Character",
                table: "Foe");
        }
    }
}
