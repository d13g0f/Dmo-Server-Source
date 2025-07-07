// Source\Distribution\DigitalWorldOnline.Game.Host\Managers\Combat\IBuffManager.cs

using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public interface IBuffManager
    {
        BuffEffect[] ApplyBuffs(GameClient client, DamageResult damageResult, byte skillSlot);
    }
}
