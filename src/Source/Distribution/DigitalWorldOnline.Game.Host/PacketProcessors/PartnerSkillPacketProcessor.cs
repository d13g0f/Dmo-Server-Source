using Microsoft.Extensions.Configuration;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Models;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using static Quartz.Logging.OperationName;
using DigitalWorldOnline.Game.Managers.Combat;
using GameServer.Logging;

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
                broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
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
                    : _skillDamageCalculator.CalculateDamage(client, skillAsset, skillSlot).FinalDamage;

                int dividedAT = client.Tamer.GodMode ? 0 : client.Partner.AT / targetMobs.Count;
                int finalDmg = rawDamage + dividedAT;

                foreach (var targetMob in targetMobs)
                {
                    if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                    if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                    if (!targetMob.InBattle)
                    {
                        broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                        targetMob.StartBattle(client.Tamer);
                    }
                    else
                    {
                        targetMob.AddTarget(client.Tamer);
                    }

                    var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

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
                    broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                    targetMob.StartBattle(client.Tamer);
                }
                else
                {
                    targetMob.AddTarget(client.Tamer);
                }

                var finalDmg = client.Tamer.GodMode
                    ? targetMob.CurrentHP
                    : _skillDamageCalculator.CalculateDamage(client, skillAsset, skillSlot).FinalDamage;

                if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                if (newHp > 0)
                {
                    broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                    broadcastAction(client.TamerId, new SkillHitPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg, targetMob.CurrentHpRate).Serialize());
                }
                else
                {
                    broadcastAction(client.TamerId, new KillOnSkillPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg).Serialize());
                    targetMob?.Die();
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
                client.Tamer.StopIBattle();
                await Task.Delay(3000);
                broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
            }

            await GameLogger.LogInfo("Fin del procesamiento del paquete PartnerSkill", "skills");
        }
    }
}
