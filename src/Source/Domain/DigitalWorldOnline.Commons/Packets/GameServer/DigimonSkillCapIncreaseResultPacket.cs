using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class DigimonSkillCapIncreaseResultPacket : PacketWriter
    {
        private const int PacketNumber = 3249;

        public DigimonSkillCapIncreaseResultPacket(DigimonSkillCapIncreaseResultEnum result, uint formSlot, DigimonEvolutionModel dEvo, uint invSlot, uint itemId)
        {
            Type(PacketNumber);
            WriteInt(result.GetHashCode());
            WriteUInt(formSlot);
            WriteBytes(dEvo.ToArray());
            WriteUInt(invSlot);
            WriteUInt(itemId);
        }
    }
}