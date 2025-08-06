// Archivo: Source\Distribution\DigitalWorldOnline.Game.Host\Managers\PartnerCombatManager.cs

using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;

namespace DigitalWorldOnline.Game.Managers
{
    public static class PartnerCombatManager
    {
        public static void AutoAttackMob(GameClient client, GameMap map)
        {
            var target = client.Tamer.TargetIMob ?? client.Tamer.TargetSummonMob;

            if (!client.Tamer.Partner.AutoAttack)
                return;

            if (!client.Tamer.Partner.IsAttacking && target != null && target.Alive && client.Tamer.Partner.Alive)
            {
                client.Tamer.Partner.SetEndAttacking(client.Tamer.Partner.AS);
                client.Tamer.SetHidden(false);

                if (!client.Tamer.InBattle)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new SetCombatOnPacket(client.Tamer.Partner.GeneralHandler).Serialize());
                    client.Tamer.StartBattle(target);
                    client.Tamer.Partner.StartAutoAttack();
                }

                if (!target.InBattle)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new SetCombatOnPacket(target.GeneralHandler).Serialize());
                    target.StartBattle(client.Tamer);
                    client.Tamer.Partner.StartAutoAttack();
                }

                var result = AttackManager.CalculateDamage(client);

                if (result.IsMiss)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new MissHitPacket(client.Tamer.Partner.GeneralHandler, target.GeneralHandler).Serialize());
                }
                else
                {
                    var finalDmg = result.Damage;

                    if (finalDmg > target.CurrentHP)
                        finalDmg = target.CurrentHP;

                    var newHp = target.ReceiveDamage(finalDmg, client.Tamer.Id);
                    var hitType = result.IsBlocked ? 2 : result.IsCritical ? 1 : 0;

                    if (newHp > 0)
                    {
                        map.BroadcastForTamerViewsAndSelf(
                            client.Tamer.Id,
                            new HitPacket(client.Tamer.Partner.GeneralHandler, target.GeneralHandler, finalDmg, target.HPValue, newHp, hitType).Serialize());
                    }
                    else
                    {
                        map.BroadcastForTamerViewsAndSelf(
                            client.Tamer.Id,
                            new KillOnHitPacket(client.Tamer.Partner.GeneralHandler, target.GeneralHandler, finalDmg, hitType).Serialize());
                        target.Die();

                        if (!map.MobsAttacking(client.Tamer.Location.MapId))
                        {
                            client.Tamer.StopBattle();
                            map.BroadcastForTamerViewsAndSelf(
                                client.Tamer.Id,
                                new SetCombatOffPacket(client.Tamer.Partner.GeneralHandler).Serialize());
                        }
                    }
                }

                client.Tamer.Partner.UpdateLastHitTime();
            }

            if (target == null || target.Dead)
            {
                client.Tamer.Partner?.StopAutoAttack();
            }
        }

        public static void AutoAttackPartner(GameClient client, GameMap map)
        {
            var targetPartner = client.Tamer.TargetPartner;

            if (!client.Tamer.Partner.AutoAttack)
                return;

            if (!client.Tamer.Partner.IsAttacking && targetPartner != null && targetPartner.Alive && client.Tamer.Partner.Alive)
            {
                client.Tamer.Partner.SetEndAttacking(client.Tamer.Partner.AS);
                client.Tamer.SetHidden(false);

                if (!client.Tamer.InBattle)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new SetCombatOnPacket(client.Tamer.Partner.GeneralHandler).Serialize());
                    client.Tamer.StartBattle(targetPartner);
                    client.Tamer.Partner.StartAutoAttack();
                }

                if (!targetPartner.Character.InBattle)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new SetCombatOnPacket(targetPartner.GeneralHandler).Serialize());
                    targetPartner.Character.StartBattle(client.Tamer.Partner);
                    client.Tamer.Partner.StartAutoAttack();
                }

                var result = AttackManager.CalculateDamage(client);

                if (result.IsMiss)
                {
                    map.BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new MissHitPacket(client.Tamer.Partner.GeneralHandler, targetPartner.GeneralHandler).Serialize());
                }
                else
                {
                    var finalDmg = result.Damage;
                    var newHp = targetPartner.ReceiveDamage(finalDmg);
                    var hitType = result.IsBlocked ? 2 : result.IsCritical ? 1 : 0;

                    if (newHp > 0)
                    {
                        map.BroadcastForTamerViewsAndSelf(
                            client.Tamer.Id,
                            new HitPacket(client.Tamer.Partner.GeneralHandler, targetPartner.GeneralHandler, finalDmg, targetPartner.HP, newHp, hitType).Serialize());
                    }
                    else
                    {
                        map.BroadcastForTamerViewsAndSelf(
                            client.Tamer.Id,
                            new KillOnHitPacket(client.Tamer.Partner.GeneralHandler, targetPartner.GeneralHandler, finalDmg, hitType).Serialize());
                        targetPartner.Character.Die();

                        if (!map.MobsAttacking(client.Tamer.Location.MapId))
                        {
                            client.Tamer.StopBattle();
                            map.BroadcastForTamerViewsAndSelf(
                                client.Tamer.Id,
                                new SetCombatOffPacket(client.Tamer.Partner.GeneralHandler).Serialize());
                        }
                    }
                }

                client.Tamer.Partner.UpdateLastHitTime();
            }

            if (targetPartner == null || !targetPartner.Alive)
            {
                client.Tamer.Partner?.StopAutoAttack();
            }
        }
    }
}
