using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using GameServer.Logging;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Interfaces;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    public class BuffManager : IBuffManager
    {
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly ISender _sender;

        public BuffManager(
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonServer,
            ISender sender)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _sender = sender;
        }

        /// <summary>
        /// Applies buffs/debuffs to the client's Partner (self-targeting).
        /// </summary>
        public BuffEffect[] ApplyBuffs(GameClient client, byte skillSlot)
        {
            return ApplyBuffs(client, (DigimonModel?)client.Partner, skillSlot);
        }

        /// <summary>
        /// Applies buffs/debuffs to a Digimon target (PvP).
        /// </summary>
        public BuffEffect[] ApplyBuffs(GameClient client, DigimonModel? target, byte skillSlot)
        {
            var effects = new List<BuffEffect>();
            if (client?.Partner == null) return effects.ToArray();

            var skillInfo = _assets.DigimonSkillInfo
                .FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            if (skillInfo == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] SkillInfo no encontrada Type={client.Partner.CurrentType} Slot={skillSlot}", "buffs");
                return effects.ToArray();
            }

            var jsonBuff = _assets.DigimonBuffsJson.FirstOrDefault(x => x.SkillCode == skillInfo.SkillId);
            if (jsonBuff == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] Buff no encontrado en JSON para SkillCode={skillInfo.SkillId}", "buffs");
                return effects.ToArray();
            }

            _ = GameLogger.LogInfo($"[BuffManager] Tamer={client.TamerId} SkillId={skillInfo.SkillId} BuffId={jsonBuff.BuffId} EffectType={jsonBuff.EffectType}", "buffs");

            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : client.PvpMap
                    ? (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data)
                    : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            int durationMs = jsonBuff.DurationMs;
            int tickMs = jsonBuff.TickIntervalMs;
            int value = jsonBuff.Value;

            if (partnerEvolution != null && partnerEvolution.Skills.Count > skillSlot)
            {
                value += partnerEvolution.Skills[skillSlot].CurrentLevel * 1;
            }

            // Apply buffs to Partner or target Digimon
            if (target != null && (jsonBuff.EffectType == BuffEffectTypeEnum.DoT || jsonBuff.EffectType == BuffEffectTypeEnum.Reflect))
            {
                // Debuffs for enemies
                var targetClient = _mapServer.Maps
                    .SelectMany(m => m.Clients)
                    .FirstOrDefault(c => c.Tamer?.Partner.GeneralHandler == target.GeneralHandler);
                if (targetClient == null || MapUtility.IsAlly(client, targetClient)) return effects.ToArray();

                var debuff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                debuff.SetBuffInfoFromJson(jsonBuff);

                if (!target.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                {
                    target.BuffList.Add(debuff);
                    effects.Add(new BuffEffect
                    {
                        BuffId = (uint)jsonBuff.BuffId,
                        SkillCode = jsonBuff.SkillCode,
                        Duration = durationMs / 1000,
                        IsDebuff = true
                    });

                    _ = GameLogger.LogInfo($"[BuffManager] Debuff aplicado a Digimon={target.GeneralHandler} BuffId={jsonBuff.BuffId}", "buffs");

                    if (jsonBuff.EffectType == BuffEffectTypeEnum.DoT)
                    {
                        Task.Run(async () =>
                        {
                            int elapsed = 0;
                            while (elapsed < durationMs)
                            {
                                await Task.Delay(tickMs);
                                if (!target.Alive) break;

                                int dmg = value;
                                if (dmg > target.HP) dmg = target.HP;

                                var newHp = target.ReceiveDamage(dmg);
                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Partner.GeneralHandler,
                                    target.GeneralHandler,
                                    jsonBuff.BuffId,
                                    target.HpRate,
                                    dmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    target.Character.Die();
                                    break;
                                }
                                elapsed += tickMs;
                            }
                        });
                    }
                    else if (jsonBuff.EffectType == BuffEffectTypeEnum.Reflect)
                    {
                        Task.Run(async () =>
                        {
                            int ticks = durationMs / 1000;
                            for (int i = 0; i < ticks; i++)
                            {
                                await Task.Delay(1000);
                                if (!target.Alive || !target.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId)) break;

                                int reflectDmg = target.AT * 3;
                                var newHp = target.ReceiveDamage(reflectDmg);
                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Partner.GeneralHandler,
                                    target.GeneralHandler,
                                    jsonBuff.BuffId,
                                    target.HpRate,
                                    reflectDmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    target.Character.Die();
                                    break;
                                }
                            }
                        });
                    }
                }
            }
            else if (jsonBuff.EffectType == BuffEffectTypeEnum.Shield || jsonBuff.EffectType == BuffEffectTypeEnum.Unbeatable || jsonBuff.EffectType == BuffEffectTypeEnum.SkillDmg)
            {
                // Buffs for self or allies
                var targetDigimon = target ?? client.Partner;
                var targetClient = _mapServer.Maps
                    .SelectMany(m => m.Clients)
                    .FirstOrDefault(c => c.Tamer?.Partner.GeneralHandler == targetDigimon.GeneralHandler);
                if (targetClient != null && !MapUtility.IsAlly(client, targetClient) && target != client.Partner) return effects.ToArray();

                var buff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                buff.SetBuffInfoFromJson(jsonBuff);

                if (!targetDigimon.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                {
                    targetDigimon.BuffList.Add(buff);
                    effects.Add(new BuffEffect
                    {
                        BuffId = (uint)jsonBuff.BuffId,
                        SkillCode = jsonBuff.SkillCode,
                        Duration = durationMs / 1000,
                        IsDebuff = false
                    });

                    _ = GameLogger.LogInfo($"[BuffManager] Buff aplicado a Digimon={targetDigimon.GeneralHandler} BuffId={jsonBuff.BuffId} EffectType={jsonBuff.EffectType}", "buffs");

                    if (jsonBuff.EffectType == BuffEffectTypeEnum.Shield)
                    {
                        targetDigimon.DamageShieldHp = value;
                        Task.Run(async () =>
                        {
                            int elapsed = 0;
                            while (elapsed < durationMs)
                            {
                                await Task.Delay(1000);
                                if (targetDigimon.DamageShieldHp <= 0)
                                {
                                    targetDigimon.BuffList.Remove(jsonBuff.BuffId);
                                    broadcastAction(client.TamerId, new RemoveBuffPacket(targetDigimon.GeneralHandler, jsonBuff.BuffId).Serialize());
                                    break;
                                }
                                elapsed += 1000;
                            }
                        });
                    }
                    else if (jsonBuff.EffectType == BuffEffectTypeEnum.Unbeatable)
                    {
                        targetDigimon.IsUnbeatable = true;
                        Task.Delay(durationMs).ContinueWith(_ =>
                        {
                            targetDigimon.IsUnbeatable = false;
                            broadcastAction(client.TamerId, new RemoveBuffPacket(targetDigimon.GeneralHandler, jsonBuff.BuffId).Serialize());
                        });
                    }
                    else if (jsonBuff.EffectType == BuffEffectTypeEnum.SkillDmg)
                    {
                        Task.Delay(durationMs).ContinueWith(_ =>
                        {
                            targetDigimon.BuffList.Remove(jsonBuff.BuffId);
                            broadcastAction(client.TamerId, new RemoveBuffPacket(targetDigimon.GeneralHandler, jsonBuff.BuffId).Serialize());
                        });
                    }

                    broadcastAction(client.TamerId, new AddBuffPacket(
                        targetDigimon.GeneralHandler,
                        (short)jsonBuff.BuffId,
                        1,
                        (short)(durationMs / 1000),
                        jsonBuff.SkillCode).Serialize());
                }
            }

            _sender.Send(new UpdateDigimonBuffListCommand(target?.BuffList ?? client.Partner.BuffList));
            return effects.ToArray();
        }

        /// <summary>
        /// Applies debuffs to a mob target (PvE).
        /// </summary>
        public BuffEffect[] ApplyBuffs(GameClient client, IMob target, byte skillSlot)
        {
            var effects = new List<BuffEffect>();
            if (client?.Partner == null || target == null) return effects.ToArray();

            var skillInfo = _assets.DigimonSkillInfo
                .FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            if (skillInfo == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] SkillInfo no encontrada Type={client.Partner.CurrentType} Slot={skillSlot}", "buffs");
                return effects.ToArray();
            }

            var jsonBuff = _assets.DigimonBuffsJson.FirstOrDefault(x => x.SkillCode == skillInfo.SkillId);
            if (jsonBuff == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] Buff no encontrado en JSON para SkillCode={skillInfo.SkillId}", "buffs");
                return effects.ToArray();
            }

            _ = GameLogger.LogInfo($"[BuffManager] Tamer={client.TamerId} SkillId={skillInfo.SkillId} BuffId={jsonBuff.BuffId} EffectType={jsonBuff.EffectType}", "buffs");

            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            int durationMs = jsonBuff.DurationMs;
            int tickMs = jsonBuff.TickIntervalMs;
            int value = jsonBuff.Value;

            if (partnerEvolution != null && partnerEvolution.Skills.Count > skillSlot)
            {
                value += partnerEvolution.Skills[skillSlot].CurrentLevel * 1;
            }

            if (jsonBuff.EffectType == BuffEffectTypeEnum.DoT || jsonBuff.EffectType == BuffEffectTypeEnum.Reflect)
            {
                var debuff = MobDebuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                debuff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x => x.BuffId == jsonBuff.BuffId));
                debuff.SetBuffInfoFromJson(jsonBuff);

                if (!target.DebuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                {
                    target.DebuffList.Buffs.Add(debuff);
                    effects.Add(new BuffEffect
                    {
                        BuffId = (uint)jsonBuff.BuffId,
                        SkillCode = jsonBuff.SkillCode,
                        Duration = durationMs / 1000,
                        IsDebuff = true
                    });

                    _ = GameLogger.LogInfo($"[BuffManager] Debuff aplicado a Mob={target.GeneralHandler} BuffId={jsonBuff.BuffId}", "buffs");

                    if (jsonBuff.EffectType == BuffEffectTypeEnum.DoT)
                    {
                        Task.Run(async () =>
                        {
                            int elapsed = 0;
                            while (elapsed < durationMs)
                            {
                                await Task.Delay(tickMs);
                                if (!target.Alive) break;

                                int dmg = value;
                                if (dmg > target.CurrentHP) dmg = target.CurrentHP;

                                var newHp = target.ReceiveDamage(dmg, client.TamerId);
                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Partner.GeneralHandler,
                                    target.GeneralHandler,
                                    jsonBuff.BuffId,
                                    target.CurrentHpRate,
                                    dmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    target.Die();
                                    break;
                                }
                                elapsed += tickMs;
                            }
                        });
                    }
                    else if (jsonBuff.EffectType == BuffEffectTypeEnum.Reflect)
                    {
                        Task.Run(async () =>
                        {
                            int ticks = durationMs / 1000;
                            for (int i = 0; i < ticks; i++)
                            {
                                await Task.Delay(1000);
                                if (!target.Alive || !target.DebuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId)) break;

                                int reflectDmg = target.ATValue * 3;
                                var newHp = target.ReceiveDamage(reflectDmg, client.TamerId);
                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Partner.GeneralHandler,
                                    target.GeneralHandler,
                                    jsonBuff.BuffId,
                                    target.CurrentHpRate,
                                    reflectDmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    target.Die();
                                    break;
                                }
                            }
                        });
                    }
                }
            }

            return effects.ToArray();
        }
      
        public BuffEffect[] ApplyBuffs(GameClient client, DamageResult result, byte skillSlot)
        {
            if (result.Target is DigimonModel digimonTarget)
                return ApplyBuffs(client, digimonTarget, skillSlot);

            if (result.Target is IMob mobTarget)
                return ApplyBuffs(client, mobTarget, skillSlot);

            return Array.Empty<BuffEffect>();
        }


    public BuffEffect[] ApplyBuffs(GameClient client, IEnumerable<object> targets, byte skillSlot)
    {
        var allEffects = new List<BuffEffect>();

        foreach (var target in targets)
        {
            BuffEffect[] effects = target switch
            {
                DigimonModel digimon => ApplyBuffs(client, digimon, skillSlot),
                IMob mob => ApplyBuffs(client, mob, skillSlot),
                _ => Array.Empty<BuffEffect>()
            };

            allEffects.AddRange(effects);
        }

        return allEffects.ToArray();
    }

        
    }
}