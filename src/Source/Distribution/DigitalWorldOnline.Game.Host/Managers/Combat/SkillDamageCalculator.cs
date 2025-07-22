using System;
using System.Linq;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public class SkillDamageCalculator : ISkillDamageCalculator
    {
        private readonly AssetsLoader _assets;
        private readonly IBuffManager _buffManager;
        private readonly ICombatBroadcaster _broadcaster;

        public SkillDamageCalculator(
            AssetsLoader assets,
            IBuffManager buffManager,
            ICombatBroadcaster broadcaster)
        {
            _assets = assets;
            _buffManager = buffManager;
            _broadcaster = broadcaster;
        }

        public DamageResult CalculateDamage(GameClient client, DigimonSkillAssetModel skillAsset, byte skillSlot)
        {
            var partner = client.Tamer.Partner;
            var target = client.Tamer.TargetIMob;

            // 1️⃣ Buscar JSON real
            var jsonSkill = _assets.DigimonSkillsJson
                .FirstOrDefault(x => x.SkillId == skillAsset.SkillId);
            if (jsonSkill == null)
                throw new InvalidOperationException($"Skill ID {skillAsset.SkillId} not found in DigimonSkillsJson");

            // 2️⃣ Nivel real de la skill
            int skillLevel = partner.Evolutions
                .First(x => x.Type == partner.CurrentType)
                .Skills[skillSlot].CurrentLevel;

            if (skillLevel < 1) skillLevel = 1;
            if (skillLevel > jsonSkill.MaxLevel) skillLevel = jsonSkill.MaxLevel;

            // 3️⃣ Daño base escalado + clones
            double baseDamage = jsonSkill.BaseDamage + (skillLevel * jsonSkill.DamagePerLevel);
            double cloneFactor = 1 + 0.43 / (144.0 / partner.Digiclone.ATValue);
            double cloneScaled = baseDamage * cloneFactor;

            // 4️⃣ Skill Factor (SCD)
            double scdBonus = partner.SCD / 10000.0;
            double scdScaled = cloneScaled * scdBonus;

            // 5️⃣ AT y SKD
            double rawBase = cloneScaled + scdScaled + partner.AT + partner.SKD;

            // 6️⃣ Clone ATLevel Bonus (si aplica)
            double cloneBonus = partner.Digiclone.ATLevel > 0 ? rawBase * 0.301 : 0.0;

            double totalBeforeBonuses = rawBase + cloneBonus;

            // 7️⃣ Bonos atributo y elemento (sobre total base)
            double attributeBonus = totalBeforeBonuses * AttackManager.GetAttributeBonus(client);
            double elementBonus = totalBeforeBonuses * AttackManager.GetElementBonus(client);

            // 8️⃣ Defensa (sin cap)
            double damageAfterDef = totalBeforeBonuses + attributeBonus + elementBonus - target.DEValue;
            if (damageAfterDef < 0) damageAfterDef = 0;

            // 9️⃣ Buff SkillDmg
            var skillDmgBuffs = partner.BuffList.Buffs
                .Where(x => x.Definition?.EffectType == BuffEffectTypeEnum.SkillDmg);

            double skillDmgBonus = 0;
            foreach (var buff in skillDmgBuffs)
                skillDmgBonus += buff.Definition.Value;

            if (skillDmgBonus > 0)
            {
                damageAfterDef *= 1.0 + (skillDmgBonus / 100.0);
                _ = GameLogger.LogInfo($"[SkillDamageCalculator] Buff SkillDmg +{skillDmgBonus}%", "buffs");
            }

            // 🔑 Log principal
            _ = GameLogger.LogInfo($"[SkillDamageCalculator] Final: base={baseDamage:F2}, clone={cloneScaled:F2}, scd={scdScaled:F2}, rawBase={rawBase:F2}, cloneBonus={cloneBonus:F2}, attr={attributeBonus:F2}, elem={elementBonus:F2}, def={target.DEValue}, FinalDamage={damageAfterDef:F2}",
                "SkillDamageCalculator");

            // 10️⃣ Resultado
            var result = new DamageResult
            {
                FinalDamage = (int)Math.Floor(damageAfterDef),
                SkillName = jsonSkill.Name
            };

            // 11️⃣ Buffs adicionales
            var effects = _buffManager.ApplyBuffs(client, result, skillSlot);
            result.Buffs = effects;

            // 12️⃣ Broadcast
            _broadcaster.BroadcastCombat(client, result, effects);

            return result;
        }
    }
}
