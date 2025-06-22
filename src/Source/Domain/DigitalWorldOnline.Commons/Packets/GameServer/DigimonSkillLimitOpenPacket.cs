using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class DigimonSkillLimitOpenPacket : PacketWriter
    {
        private const int PacketNumber = 3245;

        public DigimonSkillLimitOpenPacket(int nResult, int nEvoSlot, int itemSlot, int nItemType)
        {
            Type(PacketNumber);
            WriteInt(nResult);
            WriteInt(nEvoSlot);

            WriteInt(200);          // SkillExperience
            WriteInt(6);            // CurrentLevel
            WriteInt(0);            //
            WriteByte(3);           // SkillPoints

            for (int i = 0; i < 5; i++)
            {
                WriteByte(15);    // MaxLevel
            }

            WriteInt(itemSlot);
            WriteInt(nItemType);
        }
    }
}