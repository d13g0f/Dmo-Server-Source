using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    /// <summary>
    /// Calculates skill damage, applies buffs, and broadcasts the result.
    /// </summary>
    public interface ISkillDamageCalculator
    {
        DamageResult CalculateDamage(GameClient client, DigimonSkillAssetModel skillAsset, byte skillSlot);
    }
}
