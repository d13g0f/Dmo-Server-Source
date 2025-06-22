using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'AK_Tamer_Name' AND object_id = OBJECT_ID('Character.Tamer'))
                BEGIN
                    ALTER TABLE Character.Tamer
                    ADD CONSTRAINT AK_Tamer_Name UNIQUE (Name);
                END;
            ");

            migrationBuilder.CreateTable(
                 name: "CommandMessage",
                 schema: "Security",
                 columns: table => new
                 {
                     Id = table.Column<long>(type: "bigint", nullable: false)
                         .Annotation("SqlServer:Identity", "1, 1"),
                     Time = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "getdate()"),
                     Message = table.Column<string>(type: "varchar(200)", nullable: false),
                     CharacterId = table.Column<long>(type: "bigint", nullable: false),
                     CharacterName = table.Column<string>(type: "varchar(25)", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_CommandMessage", x => x.Id);
                     table.ForeignKey(
                         name: "FK_CommandMessage_Tamer_CharacterId",
                         column: x => x.CharacterId,
                         principalSchema: "Character",
                         principalTable: "Tamer",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                     table.ForeignKey(
                        name: "FK_CommandMessage_Tamer_CharacterName",
                        column: x => x.CharacterName,
                        principalSchema: "Character",
                        principalTable: "Tamer",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Restrict);
                 }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandMessage",
                schema: "Security");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tamer_Name",
                schema: "Character",
                table: "Tamer");
        }
    }
}
