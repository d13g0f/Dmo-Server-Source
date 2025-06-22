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

        public async Task Process(GameClient client,byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

            //var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            var targetPartner = _mapServer.GetEnemyByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

            if (targetPartner != null)
            {
                if (targetPartner.Character.PvpMap == false)
                {
                    client.Tamer.StopBattle();
                    client.Partner.StopAutoAttack();

                    client.Send(new SetCombatOffPacket(client.Partner.GeneralHandler).Serialize());
                    client.Send(new SystemMessagePacket($"Tamer {targetPartner.Name} pvp is off !!"));
                }
            }

            if (client.Tamer.PvpMap && targetPartner.Character.PvpMap && targetPartner != null)
            {
                if (DateTime.Now < client.Partner.LastHitTime.AddMilliseconds(client.Partner.AS))
                {
                    client.Partner.StartAutoAttack();
                }

                if (targetPartner.Alive)
                {
                    if (client.Partner.IsAttacking)
                    {
                        if (client.Tamer.TargetMob?.GeneralHandler != targetPartner.GeneralHandler)
                        {
                            _logger.Information($"Character {client.Tamer.Id} switched target to partner {targetPartner.Id} - {targetPartner.Name}.");

                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTarget(targetPartner);
                            client.Partner.StartAutoAttack();
                        }
                    }
                    else
                    {
                        if (DateTime.Now < client.Partner.LastHitTime.AddMilliseconds(client.Partner.AS))
                        {
                            client.Partner.StartAutoAttack();
                            return;
                        }

                        if (!client.Tamer.InBattle)
                        {
                            _logger.Information($"Character {client.Tamer.Id} engaged partner {targetPartner.Id} - {targetPartner.Name}.");
                            client.Tamer.SetHidden(false);

                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                            client.Tamer.StartBattle(targetPartner);
                        }
                        else
                        {
                            _logger.Information($"Character {client.Tamer.Id} switched to partner {targetPartner.Id} - {targetPartner.Name}.");
                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTarget(targetPartner);
                        }

                        if (!targetPartner.Character.InBattle)
                        {
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                        }

                        targetPartner.Character.StartBattle(client.Partner);

                        client.Tamer.Partner.StartAutoAttack();

                        var missed = false;

                        if (missed)
                        {
                            _logger.Information($"Partner {client.Tamer.Partner.Id} missed hit on {client.Tamer.TargetPartner.Id} - {client.Tamer.TargetPartner.Name}.");
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new MissHitPacket(attackerHandler, targetHandler).Serialize());
                        }
                        else
                        {
                            #region Hit Damage

                            var critBonusMultiplier = 0.00;
                            var blocked = false;
                            var finalDmg = CalculateFinalDamage(client, targetPartner, out critBonusMultiplier, out blocked);

                            if (finalDmg != 0 && !client.Tamer.GodMode)
                            {
                                finalDmg = DebuffReductionDamage(client, finalDmg);
                            }

                            #endregion

                            #region Take Damage

                            if (finalDmg <= 0) finalDmg = 1;

                            var newHp = targetPartner.ReceiveDamage(finalDmg);

                            var hitType = blocked ? 2 : critBonusMultiplier > 0 ? 1 : 0;

                            if (newHp > 0)
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} to partner {targetPartner?.Id} - {targetPartner?.Name}.");

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new HitPacket(attackerHandler, targetHandler, finalDmg, targetPartner.HP, newHp, hitType).Serialize());
                            }
                            else
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} killed partner {targetPartner?.Id} - {targetPartner?.Name} with {finalDmg} damage.");

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new KillOnHitPacket(attackerHandler, targetHandler, finalDmg, hitType).Serialize());

                                targetPartner.Character.Die();

                                if (!_mapServer.EnemiesAttacking(client.Tamer.Location.MapId, client.Partner.Id, client.TamerId))
                                {
                                    client.Tamer.StopBattle();

                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                                }
                            }

                            #endregion

                            client.Partner.StartAutoAttack();
                        }

                        client.Tamer.Partner.UpdateLastHitTime();
                    }
                }
                else
                {
                    if (!_mapServer.EnemiesAttacking(client.Tamer.Location.MapId, client.Partner.Id, client.TamerId))
                    {
                        client.Tamer.StopBattle();

                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                    }
                }

            }

            Action<long,byte[]> broadcastAction = client.DungeonMap
                ? (id,data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id,data)
                : client.PvpMap
                    ? (id,data) => _pvpServer.BroadcastForTamerViewsAndSelf(id,data)
                    : (id,data) => _mapServer.BroadcastForTamerViewsAndSelf(id,data);

            Func<short,long,bool> broadcastMobs = client.DungeonMap
                ? (id,data) => _dungeonServer.IMobsAttacking(id,data)
                : client.PvpMap
                    ? (id,data) => _pvpServer.IMobsAttacking(id,data)
                    : (id,data) => _mapServer.IMobsAttacking(id,data);

            var targetMob = client.DungeonMap
                ? _dungeonServer.GetIMobByHandler(client.Tamer.Location.MapId,targetHandler,client.Tamer.Id)
                : client.PvpMap
                    ? _pvpServer.GetIMobByHandler(client.Tamer.Location.MapId,targetHandler,client.Tamer.Id)
                    : _mapServer.GetIMobByHandler(client.Tamer.Location.MapId,targetHandler,client.Tamer.Id);

            if (targetMob == null || client.Partner == null)
                return;

            client.Partner.StartAutoAttack();

            if (targetMob.Alive)
            {
                if (client.Partner.IsAttacking)
                {
                    if (targetMob.GeneralHandler != targetMob.GeneralHandler)
                    {
                        client.Tamer.SetHidden(false);
                        client.Tamer.UpdateTarget(targetMob);
                        client.Partner.StartAutoAttack();
                    }
                }
                else
                {
                    client.Partner.SetEndAttacking();

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

                    client.Tamer.Partner.StartAutoAttack();

                    var missed = !client.Tamer.GodMode && client.Tamer.CanMissHit();

                    if (missed)
                    {
                        broadcastAction(client.TamerId, new MissHitPacket(attackerHandler, targetHandler).Serialize());
                    }
                    else
                    {
                        #region Hit Damage
                        var critBonusMultiplier = 0.00;
                        var blocked = false;
                        var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : AttackManager.CalculateDamage(client, out critBonusMultiplier, out blocked);

                        #endregion

                        #region Take Damage
                        if (finalDmg <= 0) finalDmg = 1;
                        if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                        var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);
                        var hitType = blocked ? 2 : critBonusMultiplier > 0 ? 1 : 0;

                        if (newHp > 0)
                        {
                            broadcastAction(
                                client.TamerId,
                                new HitPacket(attackerHandler, targetHandler, finalDmg, targetMob.HPValue, newHp, hitType).Serialize());
                        }
                        else
                        {
                            client.Partner.SetEndAttacking();
                            broadcastAction(client.TamerId, new KillOnHitPacket(attackerHandler, targetHandler, finalDmg, hitType).Serialize());

                            targetMob?.Die();

                            if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
                            {
                                client.Tamer.StopBattle(true);
                                broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                            }
                        }
                        #endregion
                    }

                    //await Task.Delay(client.Partner.AS);

                    //client.Tamer.Partner.UpdateLastHitTime();
                    client.Tamer.Partner.NextHitTime = DateTime.UtcNow.AddMilliseconds(client.Partner.AS);

                }
            }
            else
            {
                if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
                {
                    client.Tamer.StopBattle(true);
                    broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                }
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

        // PVP Damage
        private static int CalculateFinalDamage(GameClient client, DigimonModel? targetPartner, out double critBonusMultiplier, out bool blocked)
        {
            var baseDamage = (client.Tamer.Partner.AT / targetPartner.DE * 150) + UtilitiesFunctions.RandomInt(5, 50);

            if (baseDamage < 0) baseDamage = 0;

            critBonusMultiplier = 0.00;
            double critChance = client.Tamer.Partner.CC / 100;

            if (critChance >= UtilitiesFunctions.RandomDouble())
            {
                var vlrAtual = client.Tamer.Partner.CD;
                var bonusMax = 1.50;
                var expMax = 10000;

                critBonusMultiplier = (bonusMax * vlrAtual) / expMax;
            }

            blocked = targetPartner.BL >= UtilitiesFunctions.RandomDouble();

            // Level Difference
            var levelBonusMultiplier = client.Tamer.Partner.Level > targetPartner.Level ? (0.01f * (client.Tamer.Partner.Level - targetPartner.Level)) : 0;

            // Attribute Damage
            var attributeMultiplier = 0.00;

            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetPartner.BaseInfo.Attribute))
            {
                var vlrAtual = client.Tamer.Partner.GetAttributeExperience();
                var bonusMax = 1.00;
                var expMax = 10000;

                attributeMultiplier = (bonusMax * vlrAtual) / expMax;
            }
            else if (targetPartner.BaseInfo.Attribute.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                attributeMultiplier = -0.25;
            }

            // Element Damage
            var elementMultiplier = 0.00;

            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetPartner.BaseInfo.Element))
            {
                var vlrAtual = client.Tamer.Partner.GetElementExperience();
                var bonusMax = 0.50;
                var expMax = 10000;

                elementMultiplier = (bonusMax * vlrAtual) / expMax;
            }
            else if (targetPartner.BaseInfo.Element.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                elementMultiplier = -0.50;
            }

            baseDamage /= blocked ? 2 : 1;

            return (int)Math.Floor(baseDamage +
                (baseDamage * critBonusMultiplier) +
                (baseDamage * levelBonusMultiplier) +
                (baseDamage * attributeMultiplier) +
                (baseDamage * elementMultiplier));
        }

        // --------------------------------------------------------------------------------------------------------------------
    }
}

       
    
