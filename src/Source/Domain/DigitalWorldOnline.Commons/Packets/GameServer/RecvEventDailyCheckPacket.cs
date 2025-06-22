using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class RecvEventDailyCheckPacket : PacketWriter
    {
        private const int PacketNumber = 3101;

        /// <summary>
        /// Send Daily Check Event.
        /// </summary>
        public RecvEventDailyCheckPacket()
        {
            Type(PacketNumber);
        }
    }
}