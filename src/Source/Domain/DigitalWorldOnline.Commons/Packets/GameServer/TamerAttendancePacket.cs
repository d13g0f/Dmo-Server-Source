using DigitalWorldOnline.Commons.Models.Events;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class TamerAttendancePacket : PacketWriter
    {
        private const int PacketNumber = 3133;

        /// <summary>
        /// Month event info.
        /// </summary>
        /// <param name="tamerAttendance">Event info.</param>
        public TamerAttendancePacket(AttendanceRewardModel attendenceReward)
        {
            Type(PacketNumber);
            WriteByte(0);
            WriteByte(attendenceReward?.TotalDays ?? 0);
            WriteByte(0);
        }
    }
}
