using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class RecvAttendancePacket : PacketWriter
    {
        private const int PacketNumber = 3107;

        /// <summary>
        /// Send Attendance event.
        /// </summary>
        public RecvAttendancePacket(GameClient client)//int nResCode, uint nGiveItemNo, int nWorkDayHistory)
        {
            Console.WriteLine($"Reading Attendance Packet !!");

            int nResCode = 0;
            var attendenceReward = client.Tamer.AttendanceReward;
            uint nGiveItemNo = (uint)attendenceReward.LastRewardDate.Day;
            int nWorkDayHistory = attendenceReward.LastRewardDate.Day;

            if (attendenceReward.LastRewardDate.Day == attendenceReward.TotalDays)
            {
                nResCode = 1;
            }

            Console.WriteLine($"nResCode: {nResCode} | nGiveItemNo: {nGiveItemNo} | nWorkDayHistory: {nWorkDayHistory} |");

            Type(PacketNumber);
            WriteInt(nResCode);
            WriteUInt(nGiveItemNo);
            WriteInt(nWorkDayHistory);

            if (nGiveItemNo < 32)
            {
                // Month Days to give item
                Console.WriteLine($"Giving item of day: {nGiveItemNo}");
            }
            else
            {
                // No item provided
            }
        }
    }
}