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
using DigitalWorldOnline.Commons.Enums.Map;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    /// <summary>
    /// Handles the application and lifecycle of Digimon skill buffs and debuffs.
    /// </summary>
    public class BuffManager : IBuffManager
    {
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly ISender _sender;

        public BuffManager(AssetsLoader assets, MapServer mapServer, DungeonsServer dungeonServer, ISender sender)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _sender = sender;
        }

        /// <summary>
        /// Applies buffs and debuffs based on the skill used. Returns a list of applied effects for broadcast.
        /// </summary>
        public BuffEffect[] ApplyBuffs(GameClient client, DamageResult damageResult, byte skillSlot)
        {
            var effects = new List<BuffEffect>();

            // Lookup: Find the used skill and its configuration.
            var skillInfo = _assets.DigimonSkillInfo
                .FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            if (skillInfo == null) return effects.ToArray();

            var skillCode = _assets.SkillCodeInfo
                .FirstOrDefault(x => x.SkillCode == skillInfo.SkillId);
            if (skillCode == null) return effects.ToArray();
            var buff = _assets.BuffInfo
                .FirstOrDefault(x => x.SkillCode == skillCode.SkillCode);
            if (buff == null) return effects.ToArray();
            _ =GameLogger.LogInfo($"ApplyBuffs: TamerId={client.TamerId} used SkillId={skillInfo.SkillId} (Code={skillCode.SkillCode}), BuffId={buff.BuffId}", "buffs");

            

            // Extract base skill parameters.
            var skillValue = skillCode.Apply.Where(x => x.Type > 0).Take(3).ToList();
            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            var selectedMob = client.Tamer.TargetIMob;

            // Broadcast delegate: switch between Dungeon or Map context.
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            // Define lists of supported buff and debuff attribute types.
            var debuffs = new List<SkillCodeApplyAttributeEnum>
            {
                SkillCodeApplyAttributeEnum.CrowdControl,
                SkillCodeApplyAttributeEnum.DOT,
                SkillCodeApplyAttributeEnum.DOT2
            };

            var buffs = new List<SkillCodeApplyAttributeEnum>
            {
                SkillCodeApplyAttributeEnum.MS,
                SkillCodeApplyAttributeEnum.SCD,
                SkillCodeApplyAttributeEnum.CC,
                SkillCodeApplyAttributeEnum.AS,
                SkillCodeApplyAttributeEnum.AT,
                SkillCodeApplyAttributeEnum.HP,
                SkillCodeApplyAttributeEnum.DamageShield,
                SkillCodeApplyAttributeEnum.CA,
                SkillCodeApplyAttributeEnum.Unbeatable,
                SkillCodeApplyAttributeEnum.DR,
                SkillCodeApplyAttributeEnum.EV
            };

            // Iterate secondary and tertiary skill attributes (index 1 and 2).
            for (int i = 1; i <= 2; i++)
            {
                if (skillValue.Count <= i) continue;

                var attribute = skillValue[i].Attribute;

                // --- BUFFS ---
                if (buffs.Contains(attribute))
                {
                    int buffValue = skillValue[i].Value +
                        (partnerEvolution.Skills[skillSlot].CurrentLevel * skillValue[i].IncreaseValue);

                    client.Tamer.Partner.BuffValueFromBuffSkill = buffValue;

                    // Correct way: duration comes from SkillId helper.
                  int duration = GetDurationBySkillId((int)skillCode.SkillCode);

                    var newDigimonBuff = DigimonBuffModel.Create(buff.BuffId, buff.SkillId, 0, duration);
                    var activeBuff = client.Tamer.Partner.BuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);

                    switch (attribute)
                    {
                        case SkillCodeApplyAttributeEnum.DR:
                            // Reflect damage buff.
                            if (activeBuff == null)
                            {
                                newDigimonBuff.SetBuffInfo(buff);
                                client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                effects.Add(new BuffEffect
                                {
                                    BuffId = (uint)buff.BuffId,
                                    SkillId = (int)skillCode.SkillCode,
                                    Duration = duration,
                                    IsDebuff = false
                                });

                                var reflectDamageInterval = TimeSpan.FromMilliseconds(selectedMob?.ASValue ?? 1000);
                                var buffId = newDigimonBuff.BuffId;

                                Task.Run(async () =>
                                {
                                    await Task.Delay(1500);
                                    for (int j = 0; j < duration; j++)
                                    {
                                        if (selectedMob == null
                                            || !client.Tamer.Partner.BuffList.Buffs.Any(b => b.BuffId == buffId)
                                            || selectedMob.CurrentAction != MobActionEnum.Attack)
                                        {
                                            client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                            broadcastAction(client.TamerId,
                                                new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                                _ = GameLogger.LogInfo($"Buff removed: BuffId={newDigimonBuff.BuffId} for TamerId={client.TamerId}", "buffs");
                                            break;
                                        }

                                        var damageValue = selectedMob.ATValue * 3;
                                        var newHp = selectedMob.ReceiveDamage(damageValue, client.TamerId);

                                        broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                            client.Tamer.Partner.GeneralHandler,
                                            selectedMob.GeneralHandler,
                                            newDigimonBuff.BuffId,
                                            selectedMob.CurrentHpRate,
                                            damageValue,
                                            (byte)(newHp > 0 ? 0 : 1)).Serialize());

                                        if (newHp <= 0)
                                        {
                                            client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                            broadcastAction(client.TamerId,
                                                new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                            selectedMob.Die();
                                            break;
                                        }

                                        await Task.Delay(reflectDamageInterval);
                                    }
                                });
                            }
                            break;

                        case SkillCodeApplyAttributeEnum.DamageShield:
                            if (client.Tamer.Partner.DamageShieldHp == 0)
                            {
                                newDigimonBuff.SetBuffInfo(buff);
                                client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                client.Tamer.Partner.DamageShieldHp = buffValue;
                                effects.Add(new BuffEffect
                                {
                                    BuffId = (uint)buff.BuffId,
                                    SkillId = (int)skillCode.SkillCode,
                                    Duration = duration,
                                    IsDebuff = false
                                });

                                Task.Run(async () =>
                                {
                                    int remainingDuration = duration;
                                    while (remainingDuration > 0)
                                    {
                                        await Task.Delay(1000);
                                        if (client.Tamer.Partner.DamageShieldHp <= 0)
                                        {
                                            client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                            broadcastAction(client.TamerId,
                                                new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                            break;
                                        }
                                        remainingDuration--;
                                    }
                                });
                            }
                            break;

                        case SkillCodeApplyAttributeEnum.Unbeatable:
                            if (!client.Tamer.Partner.IsUnbeatable)
                            {
                                newDigimonBuff.SetBuffInfo(buff);
                                client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                client.Tamer.Partner.IsUnbeatable = true;
                                effects.Add(new BuffEffect
                                {
                                    BuffId = (uint)buff.BuffId,
                                    SkillId = (int)skillCode.SkillCode,
                                    Duration = duration,
                                    IsDebuff = false
                                });

                                Task.Delay(duration * 1000).ContinueWith(_ =>
                                {
                                    client.Tamer.Partner.IsUnbeatable = false;
                                });
                            }
                            break;

                        default:
                            if (activeBuff == null)
                            {
                                newDigimonBuff.SetBuffInfo(buff);
                                client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                effects.Add(new BuffEffect
                                {
                                    BuffId = (uint)buff.BuffId,
                                    SkillId = (int)skillCode.SkillCode,
                                    Duration = duration,
                                    IsDebuff = false
                                });
                                client.Send(new UpdateStatusPacket(client.Tamer));
                            }
                            break;
                    }
                }

                // --- DEBUFFS ---
                if (debuffs.Contains(attribute))
                {
                    int debuffValue = skillValue[i].Value +
                        (partnerEvolution.Skills[skillSlot].CurrentLevel * skillValue[i].IncreaseValue);

                    int debuffDuration = GetDurationBySkillId((int)skillCode.SkillCode);

                    var newMobDebuff = MobDebuffModel.Create(buff.BuffId, (int)skillCode.SkillCode, 0, debuffDuration);
                    newMobDebuff.SetBuffInfo(buff);

                    var activeDebuff = selectedMob?.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);

                    switch (attribute)
                    {
                        case SkillCodeApplyAttributeEnum.CrowdControl:
                            if (activeDebuff == null && selectedMob != null)
                            {
                                selectedMob.DebuffList.Buffs.Add(newMobDebuff);
                                selectedMob.UpdateCurrentAction(MobActionEnum.CrowdControl);
                                effects.Add(new BuffEffect
                                {
                                    BuffId = (uint)buff.BuffId,
                                    SkillId = (int)skillCode.SkillCode,
                                    Duration = debuffDuration,
                                    IsDebuff = true
                                });
                            }
                            break;

                        case SkillCodeApplyAttributeEnum.DOT:
                        case SkillCodeApplyAttributeEnum.DOT2:
                            if (selectedMob != null)
                            {
                                if (debuffValue > selectedMob.CurrentHP) debuffValue = selectedMob.CurrentHP;

                                if (activeDebuff != null)
                                {
                                    activeDebuff.IncreaseEndDate(debuffDuration);
                                }
                                else
                                {
                                    selectedMob.DebuffList.Buffs.Add(newMobDebuff);
                                    effects.Add(new BuffEffect
                                    {
                                        BuffId = (uint)buff.BuffId,
                                        SkillId = (int)skillCode.SkillCode,
                                        Duration = debuffDuration,
                                        IsDebuff = true
                                    });
                                }

                                Task.Delay(debuffDuration * 1000).ContinueWith(_ =>
                                {
                                    if (selectedMob == null) return;
                                    var newHp = selectedMob.ReceiveDamage(debuffValue, client.TamerId);

                                    broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                        client.Tamer.Partner.GeneralHandler,
                                        selectedMob.GeneralHandler,
                                        newMobDebuff.BuffId,
                                        selectedMob.CurrentHpRate,
                                        debuffValue,
                                        (byte)(newHp > 0 ? 0 : 1)).Serialize());

                                    if (newHp <= 0)
                                    {
                                        selectedMob.Die();
                                    }
                                });
                            }
                            break;
                    }
                }
            }

            _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
            return effects.ToArray();
        }

        /// <summary>
        /// Returns the fixed duration for buffs/debuffs based on the skill identifier.
        /// </summary>
        private int GetDurationBySkillId(int skillCode)
        {
            return skillCode switch
            {
                (int)SkillBuffAndDebuffDurationEnum.FireRocket => 5,
                (int)SkillBuffAndDebuffDurationEnum.DynamiteHead => 4,
                (int)SkillBuffAndDebuffDurationEnum.BlueThunder => 2,
                (int)SkillBuffAndDebuffDurationEnum.NeedleRain => 10,
                (int)SkillBuffAndDebuffDurationEnum.MysticBell => 3,
                (int)SkillBuffAndDebuffDurationEnum.GoldRush => 3,
                (int)SkillBuffAndDebuffDurationEnum.NeedleStinger => 15,
                (int)SkillBuffAndDebuffDurationEnum.CurseOfQueen => 10,
                (int)SkillBuffAndDebuffDurationEnum.WhiteStatue => 15,
                (int)SkillBuffAndDebuffDurationEnum.RedSun => 10,
                (int)SkillBuffAndDebuffDurationEnum.PlasmaShot => 5,
                (int)SkillBuffAndDebuffDurationEnum.ExtremeJihad => 10,
                (int)SkillBuffAndDebuffDurationEnum.MomijiOroshi => 15,
                (int)SkillBuffAndDebuffDurationEnum.Ittouryoudan => 20,
                (int)SkillBuffAndDebuffDurationEnum.ShiningGoldSolarStorm => 6,
                (int)SkillBuffAndDebuffDurationEnum.MagnaAttack => 5,
                (int)SkillBuffAndDebuffDurationEnum.PlasmaRage => 10,
                (int)SkillBuffAndDebuffDurationEnum.KyukyokuSenjin => 1,
                (int)SkillBuffAndDebuffDurationEnum.RamapageAlterBF3 => 10,
                (int)SkillBuffAndDebuffDurationEnum.DashBlutgangVengeDuke => 15,
                _ => 0
            };
        }
    }
}
