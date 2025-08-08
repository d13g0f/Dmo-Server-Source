using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Game.Models;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public interface IBuffManager
    {
        BuffEffect[] ApplyBuffs(GameClient client, DigimonModel? target, byte skillSlot);
        BuffEffect[] ApplyBuffs(GameClient client, IMob target, byte skillSlot);

        BuffEffect[] ApplyBuffs(GameClient client, DamageResult result, byte skillSlot);
        BuffEffect[] ApplyBuffs(GameClient client, IEnumerable<object> targets, byte skillSlot);


    }
}