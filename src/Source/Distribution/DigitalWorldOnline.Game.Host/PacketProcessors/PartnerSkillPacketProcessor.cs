using Microsoft.Extensions.Configuration;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Game.Managers.Combat;
using GameServer.Logging;
using DigitalWorldOnline.Commons.Packets.Chat;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public partial class PartnerSkillPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerSkill;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly ISkillDamageCalculator _skillDamageCalculator;
        private readonly IPvpSkillDamageCalculator _pvpSkillDamageCalculator;
        private readonly IBuffManager _buffManager;
        private readonly ICombatBroadcaster _combatBroadcaster;

        public PartnerSkillPacketProcessor(
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ILogger logger,
            ISender sender,
            ISkillDamageCalculator skillDamageCalculator,
            IPvpSkillDamageCalculator pvpSkillDamageCalculator,
            IBuffManager buffManager,
            ICombatBroadcaster combatBroadcaster)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _skillDamageCalculator = skillDamageCalculator;
            _pvpSkillDamageCalculator = pvpSkillDamageCalculator;
            _buffManager = buffManager;
            _combatBroadcaster = combatBroadcaster;
        }


        public async Task Process(GameClient client, byte[] packetData)
        {
            await GameLogger.LogInfo("Inicio del procesamiento del paquete PartnerSkill", "skills");

            var packet = new GamePacketReader(packetData);

            var skillSlot = packet.ReadByte();
            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

            // -----------------------------------------------------------------------------------
            // PVP PROTECT CHECK
            // -----------------------------------------------------------------------------------

            var targetEnemyDigimon  = _pvpServer.GetEnemyByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

            if (client.PvpMap && targetEnemyDigimon  != null)
            {
                if (targetEnemyDigimon .Character.PvpProtect)
                {
                    // El objetivo está protegido → NO HACEMOS DAÑO
                    await GameLogger.LogInfo($"Skill blocked: target {targetEnemyDigimon .Character.Name} has PvpProtect active.", "skills");

                    client.Send(new SystemMessagePacket($"{targetEnemyDigimon .Character.Name} is protected after respawning.").Serialize());

                    return; // CANCELAMOS SKILL
                }
            }


            await GameLogger.LogInfo($"Datos del paquete: SkillSlot={skillSlot}, AttackerHandler={attackerHandler}, TargetHandler={targetHandler}", "skills");

            if (client.Partner == null)
            {
                await GameLogger.LogWarning("El Partner del cliente es nulo", "skills");
                return;
            }

            Func<short, long, bool> broadcastMobs = client.DungeonMap
                ? (id, data) => _dungeonServer.IMobsAttacking(id, data)
                : client.PvpMap
                    ? (id, data) => _pvpServer.IMobsAttacking(id, data)
                    : (id, data) => _mapServer.IMobsAttacking(id, data);

            var skillAsset = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            if (skillAsset == null)
            {
                await GameLogger.LogWarning($"No se encontró el asset de habilidad para Type={client.Partner.CurrentType} y Slot={skillSlot}", "skills");
                return;
            }
            await GameLogger.LogInfo($"Habilidad encontrada: SkillId={skillAsset.SkillId}", "skills");

            var jsonSkill = _assets.DigimonSkillsJson.FirstOrDefault(x => x.SkillId == skillAsset.SkillId);
            if (jsonSkill == null)
            {
                await GameLogger.LogWarning($"No se encontró el JSON de habilidad para SkillId={skillAsset.SkillId}", "skills");
                return;
            }
            await GameLogger.LogInfo($"JSON de habilidad cargado: Range={jsonSkill.Range}, Target={jsonSkill.Target}", "skills");

            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            if (client.Tamer.Partner.NextSkillTimeDict.TryGetValue(skillSlot, out var nextTime) && DateTime.UtcNow < nextTime)
            {
                await GameLogger.LogInfo($"La habilidad en el slot {skillSlot} está en cooldown hasta {nextTime}", "skills");
                return;
            }
            await GameLogger.LogInfo($"Habilidad en slot {skillSlot} lista para usar", "skills");

            SkillTypeEnum GetSkillType(int attackType, int targetType, int areaOfEffect)
            {
                if (attackType == 2) return SkillTypeEnum.Implosion;
                if (areaOfEffect > 0 && (targetType == 17 || targetType == 18)) return SkillTypeEnum.TargetArea;
                return SkillTypeEnum.Single;
            }

            var skillType = GetSkillType(jsonSkill.AttackType, jsonSkill.Target, jsonSkill.AreaOfEffect);
            await GameLogger.LogInfo($"SkillType determinado: {skillType}", "skills");

            var areaOfEffect = jsonSkill.AreaOfEffect;
            var range = jsonSkill.Range;
            var targetType = jsonSkill.Target;

            Func<short, int, int, long, IMob> getMobHandler = client.DungeonMap
                ? _dungeonServer.GetNearestIMobToTarget
                : _mapServer.GetNearestIMobToTarget;

            Func<short, int, int, long, List<IMob>> getNearbyTargetMob = client.DungeonMap
                ? _dungeonServer.GetIMobsNearbyTargetMob
                : _mapServer.GetIMobsNearbyTargetMob;

            Func<Location, int, long, List<IMob>> getNearbyPartnerMob = client.DungeonMap
                ? _dungeonServer.GetIMobsNearbyPartner
                : _mapServer.GetIMobsNearbyPartner;

            if (range < 500) range = 900;

            var targetMobs = new List<IMob>();

            if (skillType == SkillTypeEnum.TargetArea)
            {
                if (targetType == 17)
                {
                    targetMobs.AddRange(getNearbyPartnerMob(client.Partner.Location, areaOfEffect, client.TamerId));
                }
                else if (targetType == 18)
                {
                    targetMobs.AddRange(getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, client.TamerId));
                }
                await GameLogger.LogInfo($"Mobs objetivo para AoE: {targetMobs.Count}", "skills");
            }
            else if (skillType == SkillTypeEnum.Implosion)
            {
                targetMobs.AddRange(getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, range, client.TamerId));
                await GameLogger.LogInfo($"Mobs objetivo para Implosion: {targetMobs.Count}", "skills");
            }
            else
            {
                var mob = getMobHandler(client.Tamer.Location.MapId, targetHandler, range, client.TamerId);
                if (mob == null)
                {
                    await GameLogger.LogWarning("No se encontró mob objetivo para Single", "skills");
                    return;
                }
                targetMobs.Add(mob);
                await GameLogger.LogInfo($"Mob objetivo único: Handler={mob.GeneralHandler}", "skills");
            }


            // =========================================================================================
            // PvP PROTECTION FILTER — Applies only if the target is a PLAYER (DigimonModel)
            // =========================================================================================

            // Nueva lista de objetivos válidos
            List<IMob> filteredTargets = new List<IMob>();

            foreach (var mob in targetMobs)
            {
                // Caso 1 — No estamos en mapa PvP → mantener todo
                if (!client.PvpMap)
                {
                    filteredTargets.Add(mob);
                    continue;
                }

                // Intentamos resolver si el handler corresponde a un jugador
                var playerTarget = _pvpServer.GetEnemyByHandler(
                    client.Tamer.Location.MapId,
                    mob.GeneralHandler,
                    client.TamerId
                );

                // Caso 2 — No es jugador → mantener (mob normal)
                if (playerTarget == null)
                {
                    filteredTargets.Add(mob);
                    continue;
                }

                // Caso 3 — Es jugador pero NO tiene protección → mantener
                if (!playerTarget.Character.PvpProtect)
                {
                    filteredTargets.Add(mob);
                    continue;
                }

                // Caso 4 — Es jugador Y está protegido → bloquear daño
                client.Send(new SystemMessagePacket($"{playerTarget.Character.Name} is protected after respawning.").Serialize());

                await GameLogger.LogInfo($"Skill damage avoided: {playerTarget.Character.Name} has PvpProtect.", "skills");
            }

            // Reemplazar lista
            targetMobs = filteredTargets;

            // Si no quedó ningún target válido, cancelar skill sin romper animación
            if (!targetMobs.Any())
            {
                await GameLogger.LogInfo("Skill canceled: All valid targets had PvP protection.", "skills");
                return;
            }


            if (!targetMobs.Any())
            {
                await GameLogger.LogWarning("No se encontraron mobs objetivo", "skills");
                return;
            }

            if (skillType == SkillTypeEnum.Single && !targetMobs.First().Alive)
            {
                await GameLogger.LogInfo($"Mob objetivo no vivo: Handler={targetMobs.First().GeneralHandler}", "skills");
                return;
            }

            client.Partner.ReceiveDamage(jsonSkill.HPUsage);
            client.Partner.UseDs(jsonSkill.DSUsage);
            await GameLogger.LogInfo($"Partner consumió HP={jsonSkill.HPUsage}, DS={jsonSkill.DSUsage}", "skills");

            var castingTime = (int)Math.Round(jsonSkill.CastingTime);
            client.Partner.SetEndCasting(castingTime);
            await GameLogger.LogInfo($"CastingTime: {castingTime}ms", "skills");

            if (!client.Tamer.InBattle)
            {
                client.Tamer.SetHidden(false);
                broadcastAction(client.TamerId, new SetCombatOnPacket(targetMobs.First().GeneralHandler).Serialize());
                client.Tamer.StartBattleWithSkill(targetMobs, skillType);
            }
            else
            {
                client.Tamer.SetHidden(false);
                client.Tamer.UpdateTargetWithSkill(targetMobs, skillType);
            }

            if (skillType == SkillTypeEnum.TargetArea)
            {
                var rawDamage = client.Tamer.GodMode
                    ? targetMobs.First().CurrentHP
                    : _skillDamageCalculator.CalculateDamage(client, skillAsset, skillSlot).Damage;

                int dividedAT = client.Tamer.GodMode ? 0 : client.Partner.AT / targetMobs.Count;
                int finalDmg = rawDamage + dividedAT;

                foreach (var targetMob in targetMobs)
                {
                    if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                    if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                    if (!targetMob.InBattle)
                    {
                        broadcastAction(client.TamerId, new SetCombatOnPacket(targetMob.GeneralHandler).Serialize());
                        targetMob.StartBattle(client.Tamer);
                    }
                    else
                    {
                        targetMob.AddTarget(client.Tamer);
                    }

                    var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                    var effects = _buffManager.ApplyBuffs(client, targetMob, skillSlot);
                    foreach (var effect in effects)
                    {
                        if (effect.IsDebuff)
                        {
                            broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                client.Partner.GeneralHandler,
                                targetMob.GeneralHandler,
                                (int)effect.BuffId,
                                targetMob.CurrentHpRate,
                                0, 0).Serialize());
                        }

                        broadcastAction(client.TamerId, new AddBuffPacket(
                            client.Partner.GeneralHandler,
                            (short)effect.BuffId,
                            effect.IsDebuff ? 2 : 1,
                           (short)effect.Duration,
                            effect.SkillCode).Serialize());
                    }


                    if (newHp <= 0)
                    {
                        targetMob?.Die();
                    }
                }

                broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                broadcastAction(client.TamerId, new AreaSkillPacket(attackerHandler, client.Partner.HpRate, targetMobs, skillSlot, finalDmg).Serialize());
            }
            else
            {
                var targetMob = targetMobs.First();

                if (!targetMob.InBattle)
                {
                    broadcastAction(client.TamerId, new SetCombatOnPacket(targetMob.GeneralHandler).Serialize());
                    targetMob.StartBattle(client.Tamer);
                }
                else
                {
                    targetMob.AddTarget(client.Tamer);
                }


                // ---------------------------------------------
                // Second-level safety check (PvP only)
                // ---------------------------------------------
                if (client.PvpMap && targetEnemyDigimon  != null && targetEnemyDigimon .Character.PvpProtect)
                {
                    await GameLogger.LogInfo("Damage blocked: PvpProtect active.", "skills");
                    return;
                }

                // =====================================================
                // PvP DAMAGE (Player target)
                // =====================================================

                int finalDmg;

                if (client.PvpMap && targetEnemyDigimon  != null)
                {
                    // Guardamos referencia para el calculator
                    var target = targetEnemyDigimon ;

                    var pvpResult = client.Tamer.GodMode
                        ? new DamageResult { Damage = targetEnemyDigimon .HP }
                        : _pvpSkillDamageCalculator.CalculateDamage(client, skillAsset, skillSlot);

                    finalDmg = pvpResult.Damage;

                    // Clamp de seguridad
                    if (finalDmg > targetEnemyDigimon .HP)
                        finalDmg = targetEnemyDigimon .HP;

                    // Aplicar daño al jugador
                    targetEnemyDigimon .ReceiveDamage(finalDmg);
                }
                else
                {
                    // =====================================================
                    // PvE fallback (Mobs)
                    // =====================================================
                    finalDmg = client.Tamer.GodMode
                        ? targetMob.CurrentHP
                        : _skillDamageCalculator.CalculateDamage(client, skillAsset, skillSlot).Damage;

                    if (finalDmg <= 0)
                        finalDmg = client.Tamer.Partner.AT;

                    if (finalDmg > targetMob.CurrentHP)
                        finalDmg = targetMob.CurrentHP;

                    targetMob.ReceiveDamage(finalDmg, client.TamerId);
                }

                var effects = _buffManager.ApplyBuffs(client, targetMob, skillSlot);
                foreach (var effect in effects)
                {
                    if (effect.IsDebuff)
                    {
                        broadcastAction(client.TamerId, new AddDotDebuffPacket(
                            client.Partner.GeneralHandler,
                            targetMob.GeneralHandler,
                            (int)effect.BuffId,
                            targetMob.CurrentHpRate,
                            0, 0).Serialize());
                    }
                    else
                    {
                        broadcastAction(client.TamerId, new AddBuffPacket(
                        client.Partner.GeneralHandler,
                        (int)effect.BuffId,
                        (int)effect.SkillCode,
                        effect.IsDebuff ? (short)2 : (short)1,
                        effect.Duration
                    ).Serialize());

                    }
                }

                // =====================================================
                // COMBAT BROADCAST
                // =====================================================

                broadcastAction(
                    client.TamerId,
                    new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize()
                );

                // PvP → solo hit visual (no kill packet)
                if (client.PvpMap && targetEnemyDigimon  != null)
                {
                    broadcastAction(
                        client.TamerId,
                        new SkillHitPacket(
                            attackerHandler,
                            targetHandler,
                            skillSlot,
                            finalDmg,
                            targetEnemyDigimon.HpRate
                        ).Serialize()
                    );
                }
                else
                {
                    // PvE → mob logic
                    if (targetMob.CurrentHP > 0)
                    {
                        broadcastAction(
                            client.TamerId,
                            new SkillHitPacket(
                                attackerHandler,
                                targetMob.GeneralHandler,
                                skillSlot,
                                finalDmg,
                                targetMob.CurrentHpRate
                            ).Serialize()
                        );
                    }
                    else
                    {
                        broadcastAction(
                            client.TamerId,
                            new KillOnSkillPacket(
                                attackerHandler,
                                targetMob.GeneralHandler,
                                skillSlot,
                                finalDmg
                            ).Serialize()
                        );

                        targetMob?.Die();
                    }
                }

            }

            var cdMs = jsonSkill.CooldownMs;
            var nextSkillTime = DateTime.UtcNow.AddMilliseconds(cdMs);
            client.Tamer.Partner.NextSkillTimeDict[skillSlot] = nextSkillTime;

            var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            if (evolution != null && cdMs >= 20000)
            {
                evolution.Skills[skillSlot].SetCooldown(cdMs / 1000);
                await _sender.Send(new UpdateEvolutionCommand(evolution));
            }

            if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId) && client.Tamer.InBattle)
            {
                client.Tamer.StopBattle();
                broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
            }

            await GameLogger.LogInfo("Fin del procesamiento del paquete PartnerSkill", "skills");
        }
    }
}
