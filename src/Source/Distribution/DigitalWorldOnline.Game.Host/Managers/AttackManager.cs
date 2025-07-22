using System;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.Managers
{
    /// <summary>
    /// Manager para ejecutar ataques melee y controlar el estado de combate.
    /// Toda la fórmula de daño está contenida aquí.
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
        /// Calcula y aplica el daño de ataque melee.
        /// Incluye hit, evasión, bloqueo, crítico, bonus de atributo/elemento/nivel y defensa.
        /// </summary>
        /// <param name="client">Jugador atacante.</param>
        /// <param name="critBonusMultiplier">Salida: multiplicador de daño crítico aplicado.</param>
        /// <param name="blocked">Salida: si el ataque fue bloqueado.</param>
        /// <returns>Daño final aplicado.</returns>
        public static int CalculateDamage(GameClient client, out double critBonusMultiplier, out bool blocked)
        {
            var partner = client.Tamer.Partner;
            var targetMob = client.Tamer.TargetIMob;

            // INPUT LOG
            _ = GameLogger.LogInfo(
                $"Input: AT={partner.AT}, CC={partner.CC}, CD={partner.CD}, HT={partner.HT}, EV={targetMob.EVValue}, BL={targetMob.BLValue}, DE={targetMob.DEValue}, AttackerLevel={partner.Level}, TargetLevel={targetMob.Level}",
                "AttackManager");

            // 1️⃣ HIT & EVASION
            double hitChance = GetHitChance(partner.HT, targetMob.EVValue, partner.Level, targetMob.Level);
            double hitRoll = UtilitiesFunctions.RandomZeroToOne();
            if (hitChance < hitRoll)
            {
                _ = GameLogger.LogInfo($"MISS → hitChance={hitChance:F4}, roll={hitRoll:F4}", "AttackManager");
                critBonusMultiplier = 1.0;
                blocked = false;
                return 0;
            }
            _ = GameLogger.LogInfo($"HIT → hitChance={hitChance:F4}, roll={hitRoll:F4}", "AttackManager");

            // 2️⃣ BASE DAMAGE con factor aleatorio 0-8%
            double baseDamage = partner.AT;
            double randomBonus = UtilitiesFunctions.RandomZeroToOne() * 0.08;
            baseDamage *= 1.0 + randomBonus;

            _ = GameLogger.LogInfo($"BaseDamage con randomBonus={randomBonus:F4}: {baseDamage:F2}", "AttackManager");

            // 3️⃣ BLOQUEO → flag para aplicar luego
            double enemyBlockChance = targetMob.BLValue / 10000.0; // Escalado a 0-1 si viene en base 0-10000
            blocked = enemyBlockChance >= UtilitiesFunctions.RandomZeroToOne();
            _ = GameLogger.LogInfo($"Block check: BlockChance={enemyBlockChance:F4} → Blocked={blocked}", "AttackManager");

            // 4️⃣ CRÍTICO → afecta solo baseDamage
            double criticalChance = Math.Min(partner.CC / 10000.0, 1.0); // 0-1
            double attributePenalty = partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute) ? 0.1 :
                                      targetMob.Attribute.HasAttributeAdvantage(partner.BaseInfo.Attribute) ? -0.25 : 0.0;
            double criticalResistance = targetMob.Level > partner.Level ? 0.01 * (targetMob.Level - partner.Level) : 0.0;
            double effectiveCritChance = Math.Max(0.0, criticalChance + attributePenalty - criticalResistance);
            bool isCritical = effectiveCritChance >= UtilitiesFunctions.RandomZeroToOne();

            double criticalBase = 0.7; // 70% extra base
            double excessCdBonus = Math.Max(0.0, ((partner.CD / 10000.0) - 1.0) * 0.1); // Cada 10% sobre 100% suma 1%
            critBonusMultiplier = isCritical ? (1.0 + criticalBase + excessCdBonus) : 1.0;

            if (isCritical)
            {
                _ = GameLogger.LogInfo($"CRITICAL → critBonusMultiplier={critBonusMultiplier:F2}", "AttackManager");
                baseDamage *= critBonusMultiplier;
            }
            else
            {
                _ = GameLogger.LogInfo($"No CRITICAL → critChance={effectiveCritChance:F4}", "AttackManager");
            }

            // 5️⃣ BONIFICADORES (atributo, elemento, nivel)
            double attributeBonus = baseDamage * GetAttributeBonus(client);
            double elementBonus = baseDamage * GetElementBonus(client);
            double levelBonus = GetLevelBonus(partner.Level, targetMob.Level, baseDamage);

            _ = GameLogger.LogInfo(
                $"Bonuses: attributeBonus={attributeBonus:F2}, elementBonus={elementBonus:F2}, levelBonus={levelBonus:F2}",
                "AttackManager");

            // 6️⃣ DEFENSA
            double totalDamage = baseDamage + attributeBonus + elementBonus + levelBonus;
            totalDamage -= targetMob.DEValue;
            if (totalDamage < 0) totalDamage = 0;

            // 7️⃣ BLOQUEO → aplicar reducción final si blockeó
            if (blocked)
            {
                totalDamage *= 0.5; // Bloqueo absorbe 50% del daño final
                _ = GameLogger.LogInfo($"BLOCKED → Damage halved: {totalDamage:F2}", "AttackManager");
            }

            _ = GameLogger.LogInfo($"FINAL → TotalDamage={totalDamage:F2}", "AttackManager");

            if (IsBattle)
            {
                SendDebugMessages(client, attributeBonus, elementBonus, totalDamage, targetMob.DEValue, isCritical, blocked);
            }

            return (int)Math.Floor(totalDamage);
        }

        private static double GetHitChance(double hit, double evasion, int atkLvl, int defLvl)
        {
            double baseHit = hit / (hit + evasion);
            double levelAdj = (atkLvl - defLvl) * 0.01; // 1% por nivel de diferencia
            return Math.Max(0.0, Math.Min(baseHit + levelAdj, 1.0));
        }

        private static double GetLevelBonus(int attackerLevel, int targetLevel, double baseDamage)
        {
            if (attackerLevel <= targetLevel) return 0.0;
            return baseDamage * 0.01 * Math.Min(attackerLevel - targetLevel, 10); // Máximo 10%
        }

        public static double GetAttributeBonus(GameClient client)
        {
            var partner = client.Tamer.Partner;
            var targetAttr = client.Tamer.TargetIMob.Attribute;

            if (partner.BaseInfo.Attribute.HasAttributeAdvantage(targetAttr))
                return Math.Min(partner.GetAttributeExperience() / 10000.0, 0.25);

            if (targetAttr.HasAttributeAdvantage(partner.BaseInfo.Attribute))
                return -0.25;

            return 0.0;
        }

        public static double GetElementBonus(GameClient client)
        {
            var partner = client.Tamer.Partner;
            var targetElement = client.Tamer.TargetIMob.Element;

            if (partner.BaseInfo.Element.HasElementAdvantage(targetElement))
                return Math.Min(partner.GetElementExperience() / 10000.0, 0.25);

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
