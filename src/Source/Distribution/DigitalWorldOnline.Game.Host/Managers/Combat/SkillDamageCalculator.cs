using System;
using System.Linq;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public class SkillDamageCalculator : ISkillDamageCalculator
    {
        private readonly AssetsLoader _assets;
        private readonly AttackManager _attackManager;
        private readonly IBuffManager _buffManager;
        private readonly ICombatBroadcaster _broadcaster;

        public SkillDamageCalculator(
            AssetsLoader assets,
            AttackManager attackManager,
            IBuffManager buffManager,
            ICombatBroadcaster broadcaster)
        {
            _assets = assets;
            _attackManager = attackManager;
            _buffManager = buffManager;
            _broadcaster = broadcaster;
        }

        public DamageResult CalculateDamage(GameClient client, DigimonSkillAssetModel skillAsset, byte skillSlot)
        {
            // 1) ID de la skill
            // Note: The SkillId comes from client packet, but all stats are validated and read from DigimonSkillsJson.

            int skillId = skillAsset.SkillId;

            // 2) Busca el JSON real
            var jsonSkill = _assets.DigimonSkillsJson.FirstOrDefault(x => x.SkillId == skillId);
            if (jsonSkill == null)
                throw new InvalidOperationException($"Skill ID {skillId} not found in DigimonSkillsJson");

            // 3) Nivel real de la skill
            int skillLevel = client.Partner.Evolutions
                .First(x => x.Type == client.Partner.CurrentType)
                .Skills[skillSlot].CurrentLevel;

            if (skillLevel < 1) skillLevel = 1;
            if (skillLevel > jsonSkill.MaxLevel) skillLevel = jsonSkill.MaxLevel;

            // 4) Daño base escalado
            double baseDamage = jsonSkill.BaseDamage + (skillLevel * jsonSkill.DamagePerLevel);

            // 5) Factor clone
            double cloneFactor = 1 + 0.43 / (144.0 / client.Partner.Digiclone.ATValue);
            double f1 = Math.Floor(baseDamage * cloneFactor);

            // 6) Factor SCD
            double skillFactor = client.Partner.SCD / 100.0;
            double added = Math.Floor(f1 * skillFactor / 100.0);

            // 7) AT y SKD
            double rawBase = f1 + added + client.Partner.AT + client.Partner.SKD;

            int cloneBonus = client.Partner.Digiclone.ATLevel > 0 ? (int)(rawBase * 0.301) : 0;

            // 8) Bonos atributo y elemento
            int attrBonus = (int)Math.Floor(f1 * AttackManager.GetAttributeDamage(client));
            int elemBonus = (int)Math.Floor(f1 * AttackManager.GetElementDamage(client));

            // 9) Defensa sin normalizar: se normaliza adentro del Core
            int defense = (int)client.Tamer.TargetIMob.DEValue;

            int dmg = DamageCoreCalculator.Calculate(
                rawBase + cloneBonus,
                attrBonus,
                elemBonus,
                0,
                defense,
                false
            );

            // 10) Resultado final
            var result = new DamageResult
            {
                FinalDamage = dmg,
                SkillName = jsonSkill.Name
            };

            // 11) Buffs
            var effects = _buffManager.ApplyBuffs(client, result, skillSlot);
            result.Buffs = effects;

            // 12) Broadcast
            _broadcaster.BroadcastCombat(client, result, effects);

            return result;
        }
    }
}
