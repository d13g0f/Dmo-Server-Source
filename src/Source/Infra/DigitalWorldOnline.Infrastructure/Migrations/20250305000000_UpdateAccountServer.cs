using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAccountServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADD COLUMNS

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'Account' AND TABLE_NAME = 'Server' AND COLUMN_NAME = 'ExperienceBurn')
                BEGIN
                    ALTER TABLE Account.Server
                    ADD ExperienceBurn INT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'Account' AND TABLE_NAME = 'Server' AND COLUMN_NAME = 'ExperienceType')
                BEGIN
                    ALTER TABLE Account.Server
                    ADD ExperienceType INT NOT NULL DEFAULT 1;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                 name: "ExperienceType",
                 schema: "Account",
                 table: "Server");

            migrationBuilder.DropColumn(
                 name: "ExperienceBurn",
                 schema: "Account",
                 table: "Server");
        }
    }
}
