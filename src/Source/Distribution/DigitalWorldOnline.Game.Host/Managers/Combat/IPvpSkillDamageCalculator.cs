using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Asset;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public interface IPvpSkillDamageCalculator
    {
        DamageResult CalculateDamage(GameClient client, DigimonSkillAssetModel skillAsset, byte skillSlot);
    }
}
