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

        public BuffEffect[] ApplyBuffs(GameClient client, DamageResult damageResult, byte skillSlot)
        {
            var effects = new List<BuffEffect>();

            // 1️⃣ Obtener SkillInfo solo para validar SkillId del cliente
            var skillInfo = _assets.DigimonSkillInfo
                .FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            if (skillInfo == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] SkillInfo no encontrada Type={client.Partner.CurrentType} Slot={skillSlot}", "buffs");
                return effects.ToArray();
            }

            int skillId = skillInfo.SkillId;

            // 2️⃣ Usar JSON como verdad para todos los datos
            var jsonBuff = _assets.DigimonBuffsJson.FirstOrDefault(x => x.SkillCode == skillId);
            if (jsonBuff == null)
            {
                _ = GameLogger.LogWarning($"[BuffManager] Buff no encontrado en JSON para SkillCode={skillId}", "buffs");
                return effects.ToArray();
            }

            _ = GameLogger.LogInfo($"[BuffManager] Tamer={client.TamerId} SkillId={skillId} BuffId={jsonBuff.BuffId} EffectType={jsonBuff.EffectType}", "buffs");

            // 3️⃣ Preparar contexto
            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            var selectedMob = client.Tamer.TargetIMob;

            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            int durationMs = jsonBuff.DurationMs;
            int tickMs = jsonBuff.TickIntervalMs;
            int value = jsonBuff.Value;

            if (partnerEvolution != null && partnerEvolution.Skills.Count > skillSlot)
            {
                // Si usas factor de escala por nivel, ajusta aquí
                value += partnerEvolution.Skills[skillSlot].CurrentLevel * 1;
            }

            // 4️⃣ Branch por EffectType
            switch (jsonBuff.EffectType)
            {
                case BuffEffectTypeEnum.DoT:
                    if (selectedMob == null) break;

                    var newDoT = MobDebuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                    newDoT.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x => x.BuffId == jsonBuff.BuffId));
                    newDoT.SetBuffInfoFromJson(jsonBuff);


                    if (!selectedMob.DebuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                    {
                        selectedMob.DebuffList.Buffs.Add(newDoT);

                        effects.Add(new BuffEffect
                        {
                            BuffId = (uint)jsonBuff.BuffId,
                            SkillCode = jsonBuff.SkillCode,
                            Duration = durationMs / 1000,
                            IsDebuff = true
                        });


                        _ = GameLogger.LogInfo($"[BuffManager] DoT aplicado BuffId={jsonBuff.BuffId} Value={value}", "buffs");

                        Task.Run(async () =>
                        {
                            int elapsed = 0;
                            while (elapsed < durationMs)
                            {
                                await Task.Delay(tickMs);

                                if (selectedMob == null) break;

                                int dmg = value;
                                if (dmg > selectedMob.CurrentHP) dmg = selectedMob.CurrentHP;

                                var newHp = selectedMob.ReceiveDamage(dmg, client.TamerId);

                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    selectedMob.GeneralHandler,
                                    jsonBuff.BuffId,
                                    selectedMob.CurrentHpRate,
                                    dmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    selectedMob.Die();
                                    break;
                                }

                                elapsed += tickMs;
                            }
                        });
                    }
                    break;

                case BuffEffectTypeEnum.Shield:
                    var shieldBuff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                    shieldBuff.SetBuffInfoFromJson(jsonBuff);
                    if (!client.Tamer.Partner.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                    {
                        client.Tamer.Partner.BuffList.Add(shieldBuff);
                        client.Tamer.Partner.DamageShieldHp = value;

                        effects.Add(new BuffEffect
                        {
                            BuffId = (uint)jsonBuff.BuffId,
                            SkillCode = jsonBuff.SkillCode,
                            Duration = durationMs / 1000,
                            IsDebuff = false
                        });

                        _ = GameLogger.LogInfo($"[BuffManager] DamageShield aplicado con HP={value}", "buffs");

                        Task.Run(async () =>
                        {
                            int elapsed = 0;
                            while (elapsed < durationMs)
                            {
                                await Task.Delay(1000);
                                if (client.Tamer.Partner.DamageShieldHp <= 0)
                                {
                                    client.Tamer.Partner.BuffList.Remove(jsonBuff.BuffId);
                                    broadcastAction(client.TamerId, new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, jsonBuff.BuffId).Serialize());
                                    break;
                                }
                                elapsed += 1000;
                            }
                        });
                    }
                    break;

                case BuffEffectTypeEnum.Reflect:
                    if (selectedMob == null) break;

                    var reflectBuff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                    reflectBuff.SetBuffInfoFromJson(jsonBuff);

                    if (!client.Tamer.Partner.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                    {
                        client.Tamer.Partner.BuffList.Add(reflectBuff);

                        effects.Add(new BuffEffect
                        {
                            BuffId = (uint)jsonBuff.BuffId,
                            SkillCode = jsonBuff.SkillCode,
                            Duration = durationMs / 1000,
                            IsDebuff = false
                        });

                        _ = GameLogger.LogInfo($"[BuffManager] DamageReflect aplicado.", "buffs");

                        Task.Run(async () =>
                        {
                            int ticks = durationMs / 1000;
                            for (int i = 0; i < ticks; i++)
                            {
                                await Task.Delay(1000);
                                if (selectedMob == null || !client.Tamer.Partner.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId)) break;

                                int reflectDmg = selectedMob.ATValue * 3;
                                var newHp = selectedMob.ReceiveDamage(reflectDmg, client.TamerId);

                                broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    selectedMob.GeneralHandler,
                                    jsonBuff.BuffId,
                                    selectedMob.CurrentHpRate,
                                    reflectDmg,
                                    (byte)(newHp <= 0 ? 1 : 0)).Serialize());

                                if (newHp <= 0)
                                {
                                    selectedMob.Die();
                                    break;
                                }
                            }
                        });
                    }
                    break;

                case BuffEffectTypeEnum.Unbeatable:
                    var unbeatableBuff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                    unbeatableBuff.SetBuffInfoFromJson(jsonBuff);

                    if (!client.Tamer.Partner.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                    {
                        client.Tamer.Partner.BuffList.Add(unbeatableBuff);
                        client.Tamer.Partner.IsUnbeatable = true;

                        effects.Add(new BuffEffect
                        {
                            BuffId = (uint)jsonBuff.BuffId,
                            SkillCode = jsonBuff.SkillCode,
                            Duration = durationMs / 1000,
                            IsDebuff = false
                        });

                        _ = GameLogger.LogInfo($"[BuffManager] Unbeatable activo.", "buffs");

                        Task.Delay(durationMs).ContinueWith(_ =>
                        {
                            client.Tamer.Partner.IsUnbeatable = false;
                        });
                    }
                    break;

                    case BuffEffectTypeEnum.SkillDmg:
                    {
                        // 1️⃣ Crear el modelo
                        var skillDmgBuff = DigimonBuffModel.Create(jsonBuff.BuffId, jsonBuff.SkillCode, 0, durationMs / 1000);
                        skillDmgBuff.Definition = jsonBuff;

                        // 2️⃣ Verificar duplicado
                        if (!client.Partner.BuffList.Buffs.Any(x => x.BuffId == jsonBuff.BuffId))
                        {
                            // 3️⃣ Añadir al buff list
                            client.Partner.BuffList.Add(skillDmgBuff);

                            effects.Add(new BuffEffect
                            {
                                BuffId = (uint)jsonBuff.BuffId,
                                SkillCode = jsonBuff.SkillCode,
                                Duration = durationMs / 1000,
                                IsDebuff = false
                            });

                            _ = GameLogger.LogInfo($"[BuffManager] SkillDmg aplicado +{jsonBuff.Value}% por {durationMs} ms", "buffs");

                            // 4️⃣ Programar expiración
                            Task.Delay(durationMs).ContinueWith(_ =>
                            {
                                client.Partner.BuffList.Remove(jsonBuff.BuffId);
                                broadcastAction(client.TamerId, new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, jsonBuff.BuffId).Serialize());
                                _ = GameLogger.LogInfo($"[BuffManager] SkillDmg expiró BuffId={jsonBuff.BuffId}", "buffs");
                            });

                            // 5️⃣ Avisar al cliente
                            broadcastAction(client.TamerId, new AddBuffPacket(
                            client.Partner.GeneralHandler,
                             _assets.BuffInfo.FirstOrDefault(x => x.BuffId == jsonBuff.BuffId),
                                1,
                                durationMs / 1000).Serialize());
                        }
                        break;
                    }

            }

            _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
            return effects.ToArray();
        }
    }
}
