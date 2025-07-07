using System;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Chat;
using GameServer.Logging;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Application;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public class CombatBroadcaster : ICombatBroadcaster
    {
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly AssetsLoader _assets; // 📌 Necesitas AssetsLoader

        public CombatBroadcaster(MapServer mapServer, DungeonsServer dungeonServer, AssetsLoader assets)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _assets = assets; // ✔️
        }

        public void BroadcastCombat(GameClient client, DamageResult result, BuffEffect[] effects)
        {
            Action<long, byte[]> broadcast = client.DungeonMap
                ? _dungeonServer.BroadcastForTamerViewsAndSelf
                : _mapServer.BroadcastForTamerViewsAndSelf;

            if (result.FinalDamage > 0 && AttackManager.IsBattle)
            {
                string skillName = result.SkillName ?? "Unknown Skill";
                string msg = $"Used {skillName} and dealt {result.FinalDamage} DMG";

                broadcast(client.TamerId, new PartyMessagePacket(client.Tamer.Partner.Name, msg).Serialize());
                _ = GameLogger.LogInfo(
                    $"[Combat] {client.Tamer.Partner.Name} used {skillName} and dealt {result.FinalDamage} damage.",
                    "combat"
                );
            }

            foreach (var e in effects)
            {
                if (e.Target == client.Tamer.Partner)
                {
                    broadcast(client.TamerId, new SkillBuffPacket(
                        client.Tamer.GeneralHandler,
                        (int)e.BuffId,
                        0,
                        e.Duration,
                        e.SkillId
                    ).Serialize());

                    _ = GameLogger.LogInfo(
                        $"[Combat] Applied buff {e.BuffId} (Skill {e.SkillId}) to Digimon {client.Tamer.GeneralHandler}. Duration: {e.Duration}s",
                        "combat"
                    );
                }
                else if (e.Target is IMob mob)
                {
                    // ✅ Busca el modelo de buff completo
                    var buff = _assets.BuffInfo.FirstOrDefault(x => x.BuffId == e.BuffId);
                    if (buff == null)
                    {
                        _= GameLogger.LogWarning(
                            $"[Combat] Could not find BuffInfoAssetModel for BuffId={e.BuffId}",
                            "combat"
                        );
                        continue;
                    }

                    broadcast(client.TamerId, new AddBuffPacket(
                        mob.GeneralHandler,
                        buff,
                        0,
                        e.Duration
                    ).Serialize());

                    _ = GameLogger.LogInfo(
                        $"[Combat] Applied debuff {e.BuffId} (Skill {e.SkillId}) to Mob {mob.GeneralHandler}. Duration: {e.Duration}s",
                        "combat"
                    );
                }
                else
                {
                    _ = GameLogger.LogWarning(
                        $"[Combat] Unknown target for BuffEffect. BuffId={e.BuffId}, SkillId={e.SkillId}",
                        "combat"
                    );
                }
            }
        }

          
    }
}
