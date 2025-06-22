using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlterTamerSkillAssetTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE name = 'BuffId' AND object_id = OBJECT_ID('Asset.TamerSkill'))
                BEGIN
                    ALTER TABLE Asset.TamerSkill
                    ADD BuffId INT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE name = 'Type' AND object_id = OBJECT_ID('Asset.TamerSkill'))
                BEGIN
                    ALTER TABLE Asset.TamerSkill
                    ADD Type INT NOT NULL DEFAULT 0;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuffId",
                schema: "Asset",
                table: "TamerSkill");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "Asset",
                table: "TamerSkill");
        }
    }
}
