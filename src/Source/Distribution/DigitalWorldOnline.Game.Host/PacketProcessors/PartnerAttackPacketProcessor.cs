using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerAttackPacketProcessor :IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerAttack;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AttackManager _attackManager;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public PartnerAttackPacketProcessor(MapServer mapServer,DungeonsServer dungeonsServer,EventServer eventServer,PvpServer pvpServer,
            ILogger logger,ISender sender,AttackManager attackManager)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _attackManager = attackManager;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

           // _logger.Information($"[PartnerAttack] Packet received: attacker={attackerHandler}, target={targetHandler}");

            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : client.PvpMap
                    ? (id, data) => _pvpServer.BroadcastForTamerViewsAndSelf(id, data)
                    : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            Func<short, long, bool> broadcastMobs = client.DungeonMap
                ? (id, data) => _dungeonServer.IMobsAttacking(id, data)
                : client.PvpMap
                    ? (id, data) => _pvpServer.IMobsAttacking(id, data)
                    : (id, data) => _mapServer.IMobsAttacking(id, data);

            // PVP TARGET
            var targetPartner = _mapServer.GetEnemyByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

            if (targetPartner != null && !targetPartner.Character.PvpMap)
            {
              //  _logger.Warning($"[PartnerAttack] Target {targetPartner.Id} is not in PVP map.");
                client.Tamer.StopBattle();
                client.Partner.StopAutoAttack();
                client.Send(new SetCombatOffPacket(client.Partner.GeneralHandler).Serialize());
                client.Send(new SystemMessagePacket($"Tamer {targetPartner.Name} pvp is off !!"));
                return;
            }

            if (client.Tamer.PvpMap && targetPartner != null && targetPartner.Character.PvpMap && targetPartner.Alive)
            {
                // ------------------------------------------------------------------
                // PVP PROTECT CHECK
                // ------------------------------------------------------------------
                if (targetPartner.Character.PvpProtect)
                {
                    client.Partner.StopAutoAttack(); // evita loops
                    client.Send(new SystemMessagePacket($"{targetPartner.Character.Name} is under PvP protection!"));
                    return; // cancelamos ataque
                }

                if (DateTime.Now < client.Partner.LastHitTime.AddMilliseconds(client.Partner.AS))
                {
                    client.Partner.StartAutoAttack();
                    return;
                }

                if (client.Partner.IsAttacking)
                {
                    if (client.Tamer.TargetPartner?.GeneralHandler != targetPartner.GeneralHandler)
                    {
                        client.Tamer.SetHidden(false);
                        client.Tamer.UpdateTarget(targetPartner);
                        client.Partner.StartAutoAttack();
                    }
                }
                else
                {
                    if (!client.Tamer.InBattle)
                    {
                        client.Tamer.SetHidden(false);
                        broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                        client.Tamer.StartBattle(targetPartner);
                    }
                    else
                    {
                        client.Tamer.SetHidden(false);
                        client.Tamer.UpdateTarget(targetPartner);
                    }

                    if (!targetPartner.Character.InBattle)
                    {
                        broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                        targetPartner.Character.StartBattle(client.Partner);
                    }

                    client.Tamer.Partner.SetEndAttacking(client.Tamer.Partner.AS);
                    client.Tamer.Partner.StartAutoAttack();

                    var damageResult = AttackManager.CalculateDamage(client);

                    if (damageResult.IsMiss)
                    {
                        broadcastAction(client.TamerId, new MissHitPacket(attackerHandler, targetHandler).Serialize());
                    }
                    else
                    {
                        var finalDmg = damageResult.Damage;
                        if (!client.Tamer.GodMode) finalDmg = DebuffReductionDamage(client, finalDmg);
                        if (finalDmg <= 0) finalDmg = 1;

                        var newHp = targetPartner.ReceiveDamage(finalDmg);
                        var hitType = damageResult.IsBlocked ? 2 : damageResult.IsCritical ? 1 : 0;

                        if (newHp > 0)
                        {
                            broadcastAction(client.TamerId, new HitPacket(attackerHandler, targetHandler, finalDmg, targetPartner.HP, newHp, hitType).Serialize());
                        }
                        else
                        {
                            broadcastAction(client.TamerId, new KillOnHitPacket(attackerHandler, targetHandler, finalDmg, hitType).Serialize());
                            targetPartner.Character.Die();

                            if (!_mapServer.EnemiesAttacking(client.Tamer.Location.MapId, client.Partner.Id, client.TamerId))
                            {
                                client.Tamer.StopBattle();
                                broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                            }
                        }
                    }

                    client.Tamer.Partner.NextHitTime = DateTime.UtcNow.AddMilliseconds(client.Partner.AS);
                }

                return;
            }

            // PVE TARGET
            var targetMob = client.DungeonMap
                ? _dungeonServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id)
                : client.PvpMap
                    ? _pvpServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id)
                    : _mapServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id);

            if (targetMob == null || !targetMob.Alive)
            {
              //  _logger.Warning($"[PartnerAttack] IMob not found or dead. Handler={targetHandler}");
                return;
            }

            if (DateTime.Now < client.Partner.LastHitTime.AddMilliseconds(client.Partner.AS))
            {
                client.Partner.StartAutoAttack();
                return;
            }

            if (client.Partner.IsAttacking)
            {
                if (client.Tamer.TargetIMob?.GeneralHandler != targetMob.GeneralHandler)
                {
                    client.Tamer.SetHidden(false);
                    client.Tamer.UpdateTarget(targetMob);
                    client.Partner.StartAutoAttack();
                }
            }
            else
            {
                client.Partner.SetEndAttacking(client.Partner.AS);

                if (!client.Tamer.InBattle)
                {
                    client.Tamer.SetHidden(false);
                    broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                    client.Tamer.StartBattle(targetMob);
                }
                else
                {
                    client.Tamer.SetHidden(false);
                    client.Tamer.UpdateTarget(targetMob);
                }

                if (!targetMob.InBattle)
                {
                    broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                    targetMob.StartBattle(client.Tamer);
                }
                else
                {
                    targetMob.AddTarget(client.Tamer);
                }

                client.Partner.StartAutoAttack();

                var damageResult = AttackManager.CalculateDamage(client);

                if (damageResult.IsMiss)
                {
                    broadcastAction(client.TamerId, new MissHitPacket(attackerHandler, targetHandler).Serialize());
                }
                else
                {

                   var finalDmg = damageResult.Damage;

                    if (finalDmg <= 0) finalDmg = 1;
                    if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                    var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);
                    var hitType = damageResult.IsBlocked ? 2 : damageResult.IsCritical ? 1 : 0;

                    if (newHp > 0)
                    {
                        broadcastAction(client.TamerId, new HitPacket(attackerHandler, targetHandler, finalDmg, targetMob.HPValue, newHp, hitType).Serialize());
                    }
                    else
                    {
                        client.Partner.SetEndAttacking();
                        broadcastAction(client.TamerId, new KillOnHitPacket(attackerHandler, targetHandler, finalDmg, hitType).Serialize());
                        targetMob.Die();

                        if (!broadcastMobs(client.Tamer.Location.MapId, client.Tamer.Id))
                        {
                            client.Tamer.StopBattle(true);
                            broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                        }
                    }
                }

                client.Tamer.Partner.NextHitTime = DateTime.UtcNow.AddMilliseconds(client.Partner.AS);
            }
        }



        // --------------------------------------------------------------------------------------------------------------------

        private static int DebuffReductionDamage(GameClient client, int finalDmg)
        {
            if (client.Tamer.Partner.DebuffList.ActiveDebuffReductionDamage())
            {
                var debuffInfo = client.Tamer.Partner.DebuffList.ActiveBuffs.Where(buff => buff.BuffInfo.SkillInfo.Apply
                    .Any(apply => apply.Attribute == SkillCodeApplyAttributeEnum.AttackPowerDown)).ToList();

                var totalValue = 0;
                var SomaValue = 0;

                foreach (var debuff in debuffInfo)
                {
                    foreach (var apply in debuff.BuffInfo.SkillInfo.Apply)
                    {
                        switch (apply.Type)
                        {
                            case SkillCodeApplyTypeEnum.Default:
                                totalValue += apply.Value;
                                break;

                            case SkillCodeApplyTypeEnum.AlsoPercent:
                            case SkillCodeApplyTypeEnum.Percent:
                                {
                                    SomaValue += apply.Value + (debuff.TypeN) * apply.IncreaseValue;

                                    double fatorReducao = SomaValue / 100;

                                    finalDmg -= (int)(finalDmg * fatorReducao);
                                }
                                break;

                            case SkillCodeApplyTypeEnum.Unknown200:
                                {
                                    SomaValue += apply.AdditionalValue;

                                    double fatorReducao = SomaValue / 100.0;

                                    finalDmg -= (int)(finalDmg * fatorReducao);
                                }
                                break;
                        }
                    }
                }
            }

            return finalDmg;
        }

    
        // --------------------------------------------------------------------------------------------------------------------
    }
}

       
    
