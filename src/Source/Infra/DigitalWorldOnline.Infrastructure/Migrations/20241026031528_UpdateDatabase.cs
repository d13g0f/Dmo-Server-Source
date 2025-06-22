using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EncyclopediaEvolutions_DigimonBaseType",
                schema: "Character",
                table: "EncyclopediaEvolutions");

            migrationBuilder.AlterColumn<decimal>(
                name: "WOSCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "WISCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "WASCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "THSCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "STSCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "NESCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LISCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LASCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ICSCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "FISCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "DASCD",
                schema: "Asset",
                table: "TitleStatus",
                type: "numeric(38,17)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)");

            migrationBuilder.AlterColumn<bool>(
                name: "InitialPacketSentOnceSent",
                schema: "Character",
                table: "Tamer",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            // ADD Columns

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE name = 'DeckBuffId' AND object_id = OBJECT_ID('Character.Tamer'))
                BEGIN
                    ALTER TABLE Character.Tamer
                    ADD DeckBuffId INT NULL;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE name = 'Type' AND object_id = OBJECT_ID('Asset.Map'))
                BEGIN
                    ALTER TABLE Asset.Map ADD Type INT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE name = 'allMaps' AND object_id = OBJECT_ID('Config.Summon'))
                BEGIN
                    ALTER TABLE Config.Summon
                    ADD allMaps INT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.UpdateData(
                schema: "Config",
                table: "Hash",
                keyColumn: "Id",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2024, 10, 26, 6, 15, 27, 916, DateTimeKind.Local).AddTicks(7852));

            migrationBuilder.UpdateData(
                schema: "Routine",
                table: "Routine",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "CreatedAt", "NextRunTime" },
                values: new object[] { new DateTime(2025, 01, 01, 0, 0, 0, 000, DateTimeKind.Local).AddTicks(2598), new DateTime(2025, 01, 01, 0, 0, 0, 0, DateTimeKind.Local) });

            migrationBuilder.CreateIndex(
                name: "IX_EncyclopediaEvolutions_DigimonBaseType",
                schema: "Character",
                table: "EncyclopediaEvolutions",
                column: "DigimonBaseType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EncyclopediaEvolutions_DigimonBaseType",
                schema: "Character",
                table: "EncyclopediaEvolutions");

            migrationBuilder.DropColumn(
                name: "DeckBuffId",
                schema: "Character",
                table: "Tamer");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "Asset",
                table: "Map");

            migrationBuilder.DropColumn(
                name: "allMaps",
                schema: "Config",
                table: "Summon");

            migrationBuilder.AlterColumn<bool>(
                name: "InitialPacketSentOnceSent",
                schema: "Character",
                table: "Tamer",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.UpdateData(
                schema: "Config",
                table: "Hash",
                keyColumn: "Id",
                keyValue: 1L,
                column: "CreatedAt",
                value: new DateTime(2024, 10, 24, 2, 35, 42, 558, DateTimeKind.Local).AddTicks(6826));

            migrationBuilder.UpdateData(
                schema: "Routine",
                table: "Routine",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "CreatedAt", "NextRunTime" },
                values: new object[] { new DateTime(2024, 10, 24, 2, 35, 42, 567, DateTimeKind.Local).AddTicks(879), new DateTime(2024, 10, 25, 0, 0, 0, 0, DateTimeKind.Local) });

            migrationBuilder.CreateIndex(
                name: "IX_EncyclopediaEvolutions_DigimonBaseType",
                schema: "Character",
                table: "EncyclopediaEvolutions",
                column: "DigimonBaseType",
                unique: true);
        }
    }
}
