using System;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.Managers
{
    /// <summary>
    /// Manager para calcular daño melee y devolver flags claros.
    /// </summary>
    public class AttackManager
    {
        private static bool isBattle = false;

        public static bool IsBattle => isBattle;

        public static void StartBattle()
        {
            isBattle = true;
        }

        public static void EndBattle()
        {
            isBattle = false;
        }

        /// <summary>
        /// Calcula daño, evasión, bloqueo y crítico.
        /// Retorna DamageResult con flags claros.
        /// </summary>
        public static DamageResult CalculateDamage(GameClient client)
        {
            var partner = client.Tamer.Partner;
            var targetMob = client.Tamer.TargetIMob;

            _ = GameLogger.LogInfo(
                $"Input: AT={partner.AT}, CC={partner.CC}, CD={partner.CD}, HT={partner.HT}, EV={targetMob.EVValue}, BL={targetMob.BLValue}, DE={targetMob.DEValue}, AttackerLevel={partner.Level}, TargetLevel={targetMob.Level}",
                "AttackManager");

            // HIT / MISS
            
            var hitResult = GetHitChance(partner.HT, targetMob.EVValue, partner.Level, targetMob.Level);
            double hitChance = hitResult.hitChance;
            double hitRoll = UtilitiesFunctions.RandomZeroToOne();
            bool isMiss = hitChance < hitRoll;

            _ = GameLogger.LogInfo(
                $"HIT FORMULA → baseHit={hitResult.baseHit:F4}, levelFactor={hitResult.levelFactor:F4}, +20% extra aplicado, finalHitChance={hitChance:F4}",
                "AttackManager");



            if (isMiss)
            {
                _ = GameLogger.LogInfo($"MISS → hitChance={hitChance:F4}, roll={hitRoll:F4}", "AttackManager");
                return new DamageResult
                {
                    Damage = 0,
                    IsCritical = false,
                    IsBlocked = false,
                    IsMiss = true
                };
            }

            _ = GameLogger.LogInfo($"HIT → hitChance={hitChance:F4}, roll={hitRoll:F4}", "AttackManager");

            // BASE DAMAGE + RNG
            double baseDamage = partner.AT;
            double randomBonus = UtilitiesFunctions.RandomZeroToOne() * 0.08;
            baseDamage *= 1.0 + randomBonus;

            _ = GameLogger.LogInfo($"BaseDamage con randomBonus={randomBonus:F4}: {baseDamage:F2}", "AttackManager");

            // BLOQUEO
            double enemyBlockChance = targetMob.BLValue / 10000.0;
            bool blocked = enemyBlockChance >= UtilitiesFunctions.RandomZeroToOne();
            _ = GameLogger.LogInfo($"Block check: BlockChance={enemyBlockChance:F4} → Blocked={blocked}", "AttackManager");

            // CRÍTICO
            double criticalChance = Math.Min(partner.CC / 10000.0, 1.0);
            double attributePenalty = partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute) ? 0.1 :
                                      targetMob.Attribute.HasAttributeAdvantage(partner.BaseInfo.Attribute) ? -0.25 : 0.0;
            double criticalResistance = targetMob.Level > partner.Level ? 0.01 * (targetMob.Level - partner.Level) : 0.0;
            double effectiveCritChance = Math.Max(0.0, criticalChance + attributePenalty - criticalResistance);
            bool isCritical = effectiveCritChance >= UtilitiesFunctions.RandomZeroToOne();

            double criticalBase = 0.7;
            double excessCdBonus = Math.Max(0.0, ((partner.CD / 10000.0) - 1.0) * 0.1);
            double critBonusMultiplier = isCritical ? (1.0 + criticalBase + excessCdBonus) : 1.0;

            if (isCritical)
            {
                _ = GameLogger.LogInfo($"CRITICAL → critBonusMultiplier={critBonusMultiplier:F2}", "AttackManager");
                baseDamage *= critBonusMultiplier;
            }
            else
            {
                _ = GameLogger.LogInfo($"No CRITICAL → critChance={effectiveCritChance:F4}", "AttackManager");
            }

            // BONIFICADORES
            double attributeBonus = baseDamage * GetAttributeBonus(client);
            double elementBonus = baseDamage * GetElementBonus(client);
            double levelBonus = GetLevelBonus(partner.Level, targetMob.Level, baseDamage);

            _ = GameLogger.LogInfo(
                $"Bonuses: attributeBonus={attributeBonus:F2}, elementBonus={elementBonus:F2}, levelBonus={levelBonus:F2}",
                "AttackManager");

            // DEFENSA
            double totalDamage = baseDamage + attributeBonus + elementBonus + levelBonus;
            totalDamage -= targetMob.DEValue;
            if (totalDamage < 0) totalDamage = 0;

            if (blocked)
            {
                totalDamage *= 0.5;
                _ = GameLogger.LogInfo($"BLOCKED → Damage halved: {totalDamage:F2}", "AttackManager");
            }

            _ = GameLogger.LogInfo($"FINAL → TotalDamage={totalDamage:F2}", "AttackManager");

            if (IsBattle)
            {
                SendDebugMessages(client, attributeBonus, elementBonus, totalDamage, targetMob.DEValue, isCritical, blocked);
            }

            return new DamageResult
            {
                Damage = (int)Math.Floor(totalDamage),
                IsCritical = isCritical,
                IsBlocked = blocked,
                IsMiss = false
            };
        }

        private static (double hitChance, double baseHit, double levelFactor) GetHitChance(double hit, double evasion, int atkLvl, int defLvl)
        {
            double baseHit = hit / (hit + evasion);
            double levelFactor = 1.0 + ((atkLvl - defLvl) * 0.03);
            double hitChance = baseHit * levelFactor;

            hitChance += 0.3;   // +30% extra hit chance

            hitChance = Math.Max(0.0, Math.Min(hitChance, 1.0));

            return (hitChance, baseHit, levelFactor);
        }



        private static double GetLevelBonus(int attackerLevel, int targetLevel, double baseDamage)
        {
            if (attackerLevel <= targetLevel) return 0.0;
            return baseDamage * 0.01 * Math.Min(attackerLevel - targetLevel, 10);
        }

        public static double GetAttributeBonus(GameClient client)
        {
            var partner = client.Tamer.Partner;
            var targetAttr = client.Tamer.TargetIMob.Attribute;

            _ = GameLogger.LogInfo(
                $"Checking Attribute Advantage: {partner.BaseInfo.Attribute} vs {targetAttr} => {partner.BaseInfo.Attribute.HasAttributeAdvantage(targetAttr)}",
                "AttackManager");

            if (partner.BaseInfo.Attribute.HasAttributeAdvantage(targetAttr))
                return Math.Min(partner.GetAttributeExperience() / 10000.0, 0.50);

            if (targetAttr.HasAttributeAdvantage(partner.BaseInfo.Attribute))
                return -0.25;

            return 0.0;
        }


        public static double GetElementBonus(GameClient client)
        {
            var partner = client.Tamer.Partner;
            var targetElement = client.Tamer.TargetIMob.Element;

            if (partner.BaseInfo.Element.HasElementAdvantage(targetElement))
                return Math.Min(partner.GetElementExperience() / 10000.0, 0.50);

            if (targetElement.HasElementAdvantage(partner.BaseInfo.Element))
                return -0.25;

            return 0.0;
        }

        private static void SendDebugMessages(GameClient client, double attr, double elem, double totalDamage, double enemyDef, bool crit, bool block)
        {
            string partnerName = client.Tamer.Partner.Name;

            client.Send(new ChatMessagePacket($"Attr: {Math.Floor(attr)}, Elem: {Math.Floor(elem)}", ChatTypeEnum.Whisper, WhisperResultEnum.Success, partnerName, partnerName));

            if (totalDamage <= 0)
            {
                client.Send(new ChatMessagePacket("Defence too high", ChatTypeEnum.Shout, partnerName).Serialize());
            }
            else
            {
                string msg = crit ? $"CRIT DMG: {Math.Floor(totalDamage)} @{enemyDef} DEF" :
                             block ? $"BLOCK DMG: {Math.Floor(totalDamage)} @{enemyDef} DEF" :
                             $"DMG: {Math.Floor(totalDamage)} @{enemyDef} DEF";

                client.Send(new ChatMessagePacket(msg, ChatTypeEnum.Shout, partnerName).Serialize());
            }
        }
    }
}
