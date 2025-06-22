using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Rounds",
                schema: "Config",
                table: "Events",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<int>(
                name: "Duration",
                schema: "Config",
                table: "EventMob",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "Round",
                schema: "Config",
                table: "EventMob",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rounds",
                schema: "Config",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Duration",
                schema: "Config",
                table: "EventMob");

            migrationBuilder.DropColumn(
                name: "Round",
                schema: "Config",
                table: "EventMob");
        }
    }
}
