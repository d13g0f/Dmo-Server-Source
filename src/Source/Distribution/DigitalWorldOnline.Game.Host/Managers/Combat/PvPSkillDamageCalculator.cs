using System;
using System.Linq;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.GameHost;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public sealed class PvPSkillDamageCalculator : IPvpSkillDamageCalculator
    {
        private readonly AssetsLoader _assets;
        private readonly IBuffManager _buffManager;
        private readonly ICombatBroadcaster _broadcaster;

        public PvPSkillDamageCalculator(
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
            var attacker = client.Tamer.Partner;
            var targetPartner = client.Tamer.TargetPartner;

            if (targetPartner == null)
                throw new InvalidOperationException("PvP damage calculation without TargetPartner");

            // 1️⃣ JSON Skill
            var jsonSkill = _assets.DigimonSkillsJson
                .FirstOrDefault(x => x.SkillId == skillAsset.SkillId);

            if (jsonSkill == null)
                throw new InvalidOperationException($"Skill ID {skillAsset.SkillId} not found in DigimonSkillsJson");

            // 2️⃣ Nivel real de la skill
            int skillLevel = attacker.Evolutions
                .First(x => x.Type == attacker.CurrentType)
                .Skills[skillSlot].CurrentLevel;

            skillLevel = Math.Clamp(skillLevel, 1, jsonSkill.MaxLevel);

            // 3️⃣ Porcentaje base de la skill (PvP)
            double basePercent =
                jsonSkill.PvpBasePercent +
                (skillLevel - 1) * jsonSkill.PvpPercentPerLevel;

            // 4️⃣ AT → bonus porcentual suave
            double atBonus = attacker.AT / 1000.0 * 0.015; // 1.5% cada 1000 AT

            // 5️⃣ Nivel → bonus leve
            double levelBonus = attacker.Level / 10.0 * 0.01; // 1% cada 10 niveles

            // 6️⃣ Atributo
            double attributeBonus = AttackManager.GetAttributeBonus(client) * 0.1;
            // Antes 50% → ahora 5%

            // 7️⃣ Elemento
            double elementBonus = AttackManager.GetElementBonus(client) * 0.1;
            // Antes 30% → ahora 3%

            // 8️⃣ Buffs PvP (si existen)
            double buffBonus = 0;
            var dmgBuffs = attacker.BuffList.Buffs
                .Where(x => x.Definition?.EffectType == BuffEffectTypeEnum.SkillDmg);

            foreach (var buff in dmgBuffs)
                buffBonus += buff.Definition.Value / 100.0;

            // 9️⃣ Defensa → mitigación porcentual
            double defense = targetPartner.DE;
            double defenseMitigation = defense / (defense + 800.0); // tunable

            // 🔢 Fórmula final
            double finalPercent =
                basePercent *
                (1 + atBonus + levelBonus + attributeBonus + elementBonus + buffBonus);

            finalPercent *= (1 - defenseMitigation);

            if (finalPercent < 0.01)
                finalPercent = 0.01; // mínimo 1%

            int finalDamage = (int)Math.Floor(finalPercent * targetPartner.HP);

            // 🔍 Log clave
            _ = GameLogger.LogInfo(
                $"[PvPSkillDamage] Skill={jsonSkill.Name} Lv={skillLevel} Base%={basePercent:P2} " +
                $"AT={atBonus:P2} LvBonus={levelBonus:P2} Attr={attributeBonus:P2} Elem={elementBonus:P2} " +
                $"DefMit={defenseMitigation:P2} FinalDmg={finalDamage}",
                "pvp-combat");

            // 10️⃣ Resultado
            var result = new DamageResult
            {
                Damage = finalDamage,
                SkillName = jsonSkill.Name,
                Target = targetPartner
            };

            var effects = _buffManager.ApplyBuffs(client, result, skillSlot);
            result.Buffs = effects;

            _broadcaster.BroadcastCombat(client, result, effects);

            return result;
        }
    }
}
