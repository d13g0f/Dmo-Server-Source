using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalWorldOnline.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAttendanceReward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADD COLUMNS

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'Event' AND TABLE_NAME = 'AttendanceReward' AND COLUMN_NAME = 'RewardClaimedToday')
                BEGIN
                    ALTER TABLE Event.AttendanceReward
                    ADD RewardClaimedToday BIT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'Account' AND TABLE_NAME = 'Account' AND COLUMN_NAME = 'DailyRewardClaimed')
                BEGIN
                    ALTER TABLE Account.Account
                    ADD DailyRewardClaimed BIT NOT NULL DEFAULT 0;
                END;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'Account' AND TABLE_NAME = 'Account' AND COLUMN_NAME = 'DailyRewardClaimedAmount')
                BEGIN
                    ALTER TABLE Account.Account
                    ADD DailyRewardClaimedAmount INT NOT NULL DEFAULT 0;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyRewardClaimedAmount",
                schema: "Account",
                table: "Account");

            migrationBuilder.DropColumn(
                name: "DailyRewardClaimed",
                schema: "Account",
                table: "Account");

            migrationBuilder.DropColumn(
                 name: "RewardClaimedToday",
                 schema: "Event",
                 table: "AttendanceReward");
        }
    }
}
