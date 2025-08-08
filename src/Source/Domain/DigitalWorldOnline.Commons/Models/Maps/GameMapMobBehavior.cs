using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Assets;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Arena;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;

namespace DigitalWorldOnline.Commons.Models.Map
{
    public sealed partial class GameMap
    {
        public static readonly List<short> DungeonMapIds = new()
        {
            17, 50, 51, 10, 210, 211, 212, 213, 214, 252, 263, 264, 300, 301,
            1110, 1111, 1112, 1304, 1308, 1310, 1311, 1403, 1404, 1406, 1500,
            1501, 1502, 1600, 1601, 1602, 1603, 1604, 1605, 1606, 1607, 1608,
            1609, 1610, 1611, 1612, 1613, 1614, 1615, 1701, 1702, 1703, 1704,
            1705, 1706, 1809, 1810, 1911, 2001, 2002, 9103, 9861
        };

        public static readonly List<short> PvpMapIds = new() { 9101 };

        public static readonly List<short> EventMapIds = new() { 
            // tus ids de evento si tienes 
        };


        private List<MobConfigModel> _mobsToDestroy = new List<MobConfigModel>();

        private List<MobConfigModel> _mobsToAdd = new List<MobConfigModel>();

        public List<long> NearestTamers(long mobId)
        {
            return ConnectedTamers.Where(x => x.MobsInView.Contains(mobId)).Select(x => x.Id).ToList();
        }

        public List<long> FarawayTamers(MobConfigModel mob)
        {
            var targetTamers = new List<long>();

            foreach (var tamer in ConnectedTamers)
            {
                var diff = UtilitiesFunctions.CalculateDistance(
                    mob.CurrentLocation.X,
                    tamer.Partner.Location.X,
                    mob.CurrentLocation.Y,
                    tamer.Partner.Location.Y);

                if (diff >= _stopSeeing)
                    targetTamers.Add(tamer.Id);
            }

            return targetTamers;
        }
        public List<long> FarawayTamers(SummonMobModel mob)
        {
            var targetTamers = new List<long>();

            foreach (var tamer in ConnectedTamers)
            {
                var diff = UtilitiesFunctions.CalculateDistance(
                    mob.CurrentLocation.X,
                    tamer.Partner.Location.X,
                    mob.CurrentLocation.Y,
                    tamer.Partner.Location.Y);

                if (diff >= _stopSeeing)
                    targetTamers.Add(tamer.Id);
            }

            return targetTamers;
        }
        // ---------------------------------------------------------------------------------------

        public void UpdateMapMobs()
        {
            if (!UpdateMobs)
                return;

            var mobsToRemove = new List<MobConfigModel>();

            foreach (var mob in Mobs)
                CheckRemoveMob(mobsToRemove, mob);

            foreach (var mob in mobsToRemove)
                RemoveMob(mob);

            //foreach (var mob in _mobsToAdd)
            //   AddMob(mob);

            FinishMobsUpdate();
        }
        
        public void UpdateMapMobs(bool summon)
        {


            var mobsToRemove = new List<SummonMobModel>();

            foreach (var mob in SummonMobs)
                CheckRemoveMob(mobsToRemove, mob);

            foreach (var mob in mobsToRemove)
                RemoveMob(mob);

            //foreach (var mob in _mobsToAdd)
            //    AddMob(mob);
        }

        public void UpdateMapMobs(List<NpcColiseumAssetModel> npcAsset)
        {
            var mobsToRemove = new List<MobConfigModel>();

            foreach (var mob in Mobs)
                CheckRemoveMob(mobsToRemove, mob, npcAsset);

            foreach (var mob in mobsToRemove)
                RemoveMob(mob);

            //foreach (var mob in _mobsToAdd)
            //   AddMob(mob);

            FinishMobsUpdate();
        }

        // ---------------------------------------------------------------------------------------

        private void CheckRemoveMob(List<MobConfigModel> mobsToRemove, MobConfigModel mob)
        {
            if (_mobsToDestroy.Contains(mob))
            {
                foreach (var view in mob.TamersViewing)
                {
                    var targetClient = Clients.FirstOrDefault(x => x.TamerId == view);

                    if (targetClient != null)
                    {
                        targetClient.Tamer.RemoveTarget(mob);

                        targetClient.Send(new DestroyMobsPacket(mob));
                    }
                }

                mob.Destroy();

                mobsToRemove.Add(mob);
            }


        }
        
        private void CheckRemoveMob(List<MobConfigModel> mobsToRemove, MobConfigModel mob,List<NpcColiseumAssetModel> npcAsset)
        {

            if (mob.CurrentAction == Enums.Map.MobActionEnum.Destroy && DateTime.Now >= mob.DieTime.AddSeconds(5))
            {
                ColiseumStageClear(mob,npcAsset);
                mobsToRemove.Add(mob);
            }

        }

        // ---------------------------------------------------------------------------------------

        private void ColiseumStageClear( MobConfigModel mob, List<NpcColiseumAssetModel> npcAsset)
        {
            if (ColiseumMobs.Contains((int)mob.Id))
            {
               ColiseumMobs.Remove((int)mob.Id);

                if (ColiseumMobs.Count == 1)
                {
                    var npcInfo = npcAsset.FirstOrDefault(x => x.NpcId == ColiseumMobs.First());

                    if (npcInfo != null)
                    {
                        foreach (var player in Clients.Where(x => x.Tamer.Partner.Alive))
                        {
                            player.Tamer.Points.IncreaseAmount(npcInfo.MobInfo[player.Tamer.Points.CurrentStage - 1].WinPoints);
                            player?.Send(new DungeonArenaStageClearPacket(mob.Type, mob.TargetTamer.Points.CurrentStage, mob.TargetTamer.Points.Amount, npcInfo.MobInfo[mob.TargetTamer.Points.CurrentStage -1].WinPoints, ColiseumMobs.First()));

                        }

                    }
                }
            }
        }

        private void CheckRemoveMob(List<SummonMobModel> mobsToRemove, SummonMobModel mob)
        {
            if (mob.RemainingMinutes == 0 || mob.CurrentAction == Enums.Map.MobActionEnum.Destroy)
            {
                foreach (var view in mob.TamersViewing)
                {
                    var targetClient = Clients.FirstOrDefault(x => x.TamerId == view);

                    if (targetClient != null)
                    {
                        targetClient.Tamer.RemoveTarget(mob);


                        if (!mob.Dead)
                        {
                            targetClient.Tamer.StopBattle(true);

                            BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOffPacket(targetClient.Tamer.GeneralHandler).Serialize());
                            BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOffPacket(targetClient.Tamer.Partner.GeneralHandler).Serialize());
                            targetClient?.Send(new UnloadMobsPacket(mob));

                            // targetClient.Send(new DestroyMobsPacket(mob)); 
                        }
                    }
                }

                mob.Destroy();

                mobsToRemove.Add(mob);
            }
        }

        // ---------------------------------------------------------------------------------------


        public void AttackTarget(MobConfigModel mob, List<NpcColiseumAssetModel> npcAsset)
        {
            #region Hit Damage
            // Registrar valores de entrada
         

            // Daño base
            double baseDamage = mob.ATValue - mob.Target.DE + UtilitiesFunctions.RandomInt(1, 15);
            if (baseDamage < 0) baseDamage = 0;

            // Bloqueo
            double enemyBlockChance = mob.Target.BL;
            bool blocked = enemyBlockChance >= UtilitiesFunctions.RandomDouble();
            if (blocked)
            {
                baseDamage /= 2; // Bloqueo reduce daño a la mitad

            }

            // Cálculo de crítico
            double critBonusMultiplier = 1.0;
            double criticalChance = Math.Min(mob.CTValue / 100.0, 2.0); // Cap a 200%
            double attributePenalty = mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.1 :
                                     mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0;
            double criticalResistance = 0.0; // Resistencia crítica del objetivo
            double effectiveCriticalChance = Math.Max(0.0, criticalChance + attributePenalty - criticalResistance);
            bool isCritical = effectiveCriticalChance >= UtilitiesFunctions.RandomDouble();

           double criticalDamageBonus = mob.CDValue / 100.0; // CD como porcentaje
           double excessCdBonus = Math.Max(0.0, (mob.CDValue - 100.0) * 0.005); // +0.5% por cada 1% sobre 100
            if (isCritical)
            {
                blocked = false; // Crítico anula bloqueo
                critBonusMultiplier = 1.0 + criticalDamageBonus + excessCdBonus;
                baseDamage *= critBonusMultiplier;
              
            }
           // else
           // {
            //    Console.WriteLine($"No critical hit: effectiveCriticalChance={effectiveCriticalChance:F4}");
           // }

            // Bonificaciones
            double levelBonus = mob.Level > mob.Target.Level ? baseDamage * 0.01 * (mob.Level - mob.Target.Level) : 0;
            double attributeBonus = baseDamage * (mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.25 :
                                                  mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0);
            double elementBonus = baseDamage * (mob.Element.HasElementAdvantage(mob.Target.BaseInfo.Element) ? 0.25 :
                                               mob.Target.BaseInfo.Element.HasElementAdvantage(mob.Element) ? -0.25 : 0.0);

            

            // Daño final
            int finalDmg = (int)Math.Floor(baseDamage + levelBonus + attributeBonus + elementBonus);
            if (finalDmg <= 0) finalDmg = 1;
      
            #endregion

            var previousHp = mob.Target.CurrentHp;
            var newHp = mob.Target.ReceiveDamage(finalDmg);

            var hitType = blocked ? 2 : critBonusMultiplier > 1.0 ? 1 : 0;

            if (newHp > 0)
            {
                BroadcastForTargetTamers(mob.TamersViewing, new HitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, previousHp, newHp, hitType).Serialize());
            }
            else
            {
                BroadcastForTargetTamers(mob.TamersViewing, new KillOnHitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, hitType).Serialize());
                BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOffPacket(mob.TargetHandler).Serialize());

                if (mob.TargetTamer != null && ColiseumMobs.Contains((int)mob.Id))
                {
                    var npcInfo = npcAsset.FirstOrDefault(x => x.NpcId == ColiseumMobs.First());
                    if (npcInfo != null)
                    {
                        mob.TargetTamer.Points.ReductionAmount(npcInfo.MobInfo[mob.TargetTamer.Points.CurrentStage - 1].LosePoints);
                    }
                }

                mob.TargetTamer?.Die();
                mob.NextTarget();
            }

            mob.UpdateLastHit();
            mob.UpdateLastHitTry();
        }

        public void AttackTarget(SummonMobModel mob)
        {
            #region Hit Damage
            // Registrar valores de entrada
       
            // Daño base
            double baseDamage = mob.ATValue - mob.Target.DE + UtilitiesFunctions.RandomInt(1, 15);
            if (baseDamage < 0) baseDamage = 0;
         
            // Bloqueo
            double enemyBlockChance = mob.Target.BL;
            bool blocked = enemyBlockChance >= UtilitiesFunctions.RandomDouble();
            if (blocked)
            {
                baseDamage /= 2; // Bloqueo reduce daño a la mitad
        
            }

            // Cálculo de crítico
            double critBonusMultiplier = 1.0;
            double criticalChance = Math.Min(mob.CTValue / 100.0, 2.0); // Cap a 200%
            double attributePenalty = mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.1 :
                                     mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0;
            double criticalResistance = 0.0; // Resistencia crítica del objetivo
            double effectiveCriticalChance = Math.Max(0.0, criticalChance + attributePenalty - criticalResistance);
            bool isCritical = effectiveCriticalChance >= UtilitiesFunctions.RandomDouble();

            double criticalDamageBonus = mob.CDValue / 100.0; // CD como porcentaje
            double excessCdBonus = Math.Max(0.0, (mob.CDValue - 100.0) * 0.005); // +0.5% por cada 1% sobre 100
            if (isCritical)
            {
                blocked = false; // Crítico anula bloqueo
                critBonusMultiplier = 1.0 + criticalDamageBonus + excessCdBonus;
                baseDamage *= critBonusMultiplier;
                
            }
           // else
          //  {
            //    Console.WriteLine($"No critical hit: effectiveCriticalChance={effectiveCriticalChance:F4}");
           // }

            // Bonificaciones
            double levelBonus = mob.Level > mob.Target.Level ? baseDamage * 0.01 * (mob.Level - mob.Target.Level) : 0;
            double attributeBonus = baseDamage * (mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.25 :
                                                  mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0);
            double elementBonus = baseDamage * (mob.Element.HasElementAdvantage(mob.Target.BaseInfo.Element) ? 0.25 :
                                               mob.Target.BaseInfo.Element.HasElementAdvantage(mob.Element) ? -0.25 : 0.0);

           // Console.WriteLine( "AttackManager",
              //  $"Bonuses: levelBonus={levelBonus:F2}, attributeBonus={attributeBonus:F2}, elementBonus={elementBonus:F2}");

            // Daño final
            int finalDmg = (int)Math.Floor(baseDamage + levelBonus + attributeBonus + elementBonus);
            if (finalDmg <= 0) finalDmg = 1;
           // Console.WriteLine( "AttackManager",
           //     $"Final damage: finalDmg={finalDmg}, blocked={blocked}, isCritical={isCritical}");
            #endregion

            var previousHp = mob.Target.CurrentHp;
            var newHp = mob.Target.ReceiveDamage(finalDmg);

            var hitType = blocked ? 2 : critBonusMultiplier > 1.0 ? 1 : 0;

            if (newHp > 0)
            {
                BroadcastForTargetTamers(mob.TamersViewing, new HitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, previousHp, newHp, hitType).Serialize());
            }
            else
            {
                BroadcastForTargetTamers(mob.TamersViewing, new KillOnHitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, hitType).Serialize());
                BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOffPacket(mob.TargetHandler).Serialize());

                mob.TargetTamer?.Die();
                mob.NextTarget();
            }

            mob.UpdateLastHit();
            mob.UpdateLastHitTry();
        }

        public void AttackTarget(EventMobConfigModel mob)
        {
            #region Hit Damage
            // Registrar valores de entrada
           // Console.WriteLine( "AttackManager",
             //   $"Input: MobType=EventMobConfigModel, AT={mob.ATValue}, CT={mob.CTValue}, CD={mob.CDValue}, TargetDE={mob.Target.DE}, TargetBL={mob.Target.BL}, TargetLevel={mob.Target.Level}");

            // Daño base
            double baseDamage = mob.ATValue - mob.Target.DE + UtilitiesFunctions.RandomInt(1, 15);
            if (baseDamage < 0) baseDamage = 0;
          //  Console.WriteLine( "AttackManager",
            //    $"Base damage: baseDamage={baseDamage:F2}, RandomBonus={UtilitiesFunctions.RandomInt(1, 15)}");

            // Bloqueo
            double enemyBlockChance = mob.Target.BL;
            bool blocked = enemyBlockChance >= UtilitiesFunctions.RandomDouble();
            if (blocked)
            {
                baseDamage /= 2; // Bloqueo reduce daño a la mitad
                //Console.WriteLine( "AttackManager",
                //    $"Blocked: baseDamage halved to {baseDamage:F2}");
            }

            // Cálculo de crítico
            double critBonusMultiplier = 1.0;
            double criticalChance = Math.Min(mob.CTValue / 100.0, 2.0); // Cap a 200%
            double attributePenalty = mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.1 :
                                     mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0;
            double criticalResistance = 0.0; // Resistencia crítica del objetivo
            double effectiveCriticalChance = Math.Max(0.0, criticalChance + attributePenalty - criticalResistance);
            bool isCritical = effectiveCriticalChance >= UtilitiesFunctions.RandomDouble();

           double criticalDamageBonus = mob.CDValue / 100.0; // CD como porcentaje
           double excessCdBonus = Math.Max(0.0, (mob.CDValue - 100.0) * 0.005); // +0.5% por cada 1% sobre 100
            if (isCritical)
            {
                blocked = false; // Crítico anula bloqueo
                critBonusMultiplier = 1.0 + criticalDamageBonus + excessCdBonus;
                baseDamage *= critBonusMultiplier;
                //Console.WriteLine( "AttackManager",
                  //  $"Critical hit: effectiveCriticalChance={effectiveCriticalChance:F4}, critBonusMultiplier={critBonusMultiplier:F2}, baseDamage={baseDamage:F2}");
            }
           // else
          //  {
           //     Console.WriteLine( "AttackManager",
            //        $"No critical hit: effectiveCriticalChance={effectiveCriticalChance:F4}");
          //  }

            // Bonificaciones
            double levelBonus = mob.Level > mob.Target.Level ? baseDamage * 0.01 * (mob.Level - mob.Target.Level) : 0;
            double attributeBonus = baseDamage * (mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute) ? 0.25 :
                                                  mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute) ? -0.25 : 0.0);
            double elementBonus = baseDamage * (mob.Element.HasElementAdvantage(mob.Target.BaseInfo.Element) ? 0.25 :
                                               mob.Target.BaseInfo.Element.HasElementAdvantage(mob.Element) ? -0.25 : 0.0);

           // Console.WriteLine("AttackManager",
            //    $"Bonuses: levelBonus={levelBonus:F2}, attributeBonus={attributeBonus:F2}, elementBonus={elementBonus:F2}");

            // Daño final
            int finalDmg = (int)Math.Floor(baseDamage + levelBonus + attributeBonus + elementBonus);
            if (finalDmg <= 0) finalDmg = 1;
          //  Console.WriteLine("AttackManager",
          //      $"Final damage: finalDmg={finalDmg}, blocked={blocked}, isCritical={isCritical}");
            #endregion

            var previousHp = mob.Target.CurrentHp;
            var newHp = mob.Target.ReceiveDamage(finalDmg);

            var hitType = blocked ? 2 : critBonusMultiplier > 1.0 ? 1 : 0;

            if (newHp > 0)
            {
                BroadcastForTargetTamers(mob.TamersViewing, new HitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, previousHp, newHp, hitType).Serialize());
            }
            else
            {
                BroadcastForTargetTamers(mob.TamersViewing, new KillOnHitPacket(mob.GeneralHandler, mob.TargetHandler, finalDmg, hitType).Serialize());
                BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOffPacket(mob.TargetHandler).Serialize());

                mob.TargetTamer?.Die();
                mob.NextTarget();
            }

            mob.UpdateLastHit();
            mob.UpdateLastHitTry();
        }
    

        
        // ---------------------------------------------------------------------------------------

        public async void SkillTarget(MobConfigModel mob, MonsterSkillInfoAssetModel? targetSkill, List<NpcColiseumAssetModel> npcAsset)
        {
            switch (targetSkill.SkillType)
            {
                case 27045:
                    {
                        List<CharacterModel> targetTamers = new List<CharacterModel>();

                        var finalDamage = targetSkill.MaxValue;

                        // Crie uma cópia da lista mob.TargetTamers para iterar sobre ela
                        var targetTamersCopy = new List<CharacterModel>(mob.TargetTamers);

                        foreach (var target in targetTamersCopy)
                        {
                            var clientToModify = Clients.FirstOrDefault(x => x.Tamer.Partner.Id == target.Partner.Id);

                            if (clientToModify != null)
                            {
                                var diff = UtilitiesFunctions.CalculateDistance(
                                    mob.CurrentLocation.X,
                                  clientToModify.Partner.Location.X,
                                    mob.CurrentLocation.Y,
                                    clientToModify.Partner.Location.Y);


                                if (diff <= 1900)
                                {
                                    var newHp = clientToModify.Partner.ReceiveDamage(finalDamage);

                                    targetTamers.Add(target);

                                    if (newHp <= 0)
                                    {
                                        if (mob.TargetTamer != null)
                                        {
                                            if (ColiseumMobs.Contains((int)mob.Id))
                                            {
                                                var npcInfo = npcAsset.FirstOrDefault(x => x.NpcId == ColiseumMobs.First());

                                                if (npcInfo != null)
                                                {
                                                    clientToModify.Tamer.Points.ReductionAmount(npcInfo.MobInfo[mob.TargetTamer.Points.CurrentStage - 1].LosePoints);
                                                }
                                            }
                                        }

                                        clientToModify?.Partner.Die();

                                    }
                                }
                            }

                            if (!targetTamers.Any())
                            {
                                ChaseTarget(mob);
                            }
                            else
                            {
                                // TODO: Fornecer tempo de espera da skill.

                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillVisualPacket(mob.GeneralHandler, targetSkill.SkillId).Serialize());
                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillDamagePacket(mob.GeneralHandler, targetSkill.SkillId, finalDamage, targetTamers).Serialize());

                                Task.Run(() =>
                                {
                                    Thread.Sleep((int)targetSkill.AnimationDelay);
                                });


                            }
                        }
                    }
                    break;
            }

            mob.SetSkillCooldown(targetSkill.Cooldown);
            mob.UpdateLastSkill();
            mob.UpdateLastSkillTry();

            if (!mob.Target.Alive)
            {
                mob.NextTarget();
            }

        }
        public async void SkillTarget(SummonMobModel mob, MonsterSkillInfoAssetModel? targetSkill)
        {
            switch (targetSkill.SkillType)
            {
                case 27045:
                    {
                        List<CharacterModel> targetTamers = new List<CharacterModel>();

                        var finalDamage = targetSkill.MaxValue;

                        // Crie uma cópia da lista mob.TargetTamers para iterar sobre ela
                        var targetTamersCopy = new List<CharacterModel>(mob.TargetTamers);

                        foreach (var target in targetTamersCopy)
                        {
                            var clientToModify = Clients.FirstOrDefault(x => x.Tamer.Partner.Id == target.Partner.Id).Partner;

                            if (clientToModify != null)
                            {
                                var diff = UtilitiesFunctions.CalculateDistance(
                                    mob.CurrentLocation.X,
                                  clientToModify.Location.X,
                                    mob.CurrentLocation.Y,
                                    clientToModify.Location.Y);


                                if (diff <= 1900)
                                {
                                    var newHp = clientToModify.ReceiveDamage(finalDamage);

                                    targetTamers.Add(target);

                                    if (newHp <= 0)
                                    {
                                        clientToModify?.Die();

                                    }
                                }
                            }

                            if (!targetTamers.Any())
                            {
                                ChaseTarget(mob);
                            }
                            else
                            {
                                // TODO: Fornecer tempo de espera da skill.

                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillVisualPacket(mob.GeneralHandler, targetSkill.SkillId).Serialize());
                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillDamagePacket(mob.GeneralHandler, targetSkill.SkillId, finalDamage, targetTamers).Serialize());

                                Task.Run(() =>
                                {
                                    Thread.Sleep((int)targetSkill.AnimationDelay);
                                });


                            }
                        }
                    }
                    break;
            }

            mob.SetSkillCooldown(targetSkill.Cooldown);
            mob.UpdateLastSkill();
            mob.UpdateLastSkillTry();

            if (!mob.Target.Alive)
            {
                mob.NextTarget();
            }

        }
        public async void SkillTarget(EventMobConfigModel mob, MonsterSkillInfoAssetModel? targetSkill)
        {
            switch (targetSkill.SkillType)
            {
                case 27045:
                    {
                        List<CharacterModel> targetTamers = new List<CharacterModel>();

                        var finalDamage = targetSkill.MaxValue;

                        // Crie uma cópia da lista mob.TargetTamers para iterar sobre ela
                        var targetTamersCopy = new List<CharacterModel>(mob.TargetTamers);

                        foreach (var target in targetTamersCopy)
                        {
                            var clientToModify = Clients.FirstOrDefault(x => x.Tamer.Partner.Id == target.Partner.Id).Partner;

                            if (clientToModify != null)
                            {
                                var diff = UtilitiesFunctions.CalculateDistance(
                                    mob.CurrentLocation.X,
                                  clientToModify.Location.X,
                                    mob.CurrentLocation.Y,
                                    clientToModify.Location.Y);


                                if (diff <= 1900)
                                {
                                    var newHp = clientToModify.ReceiveDamage(finalDamage);

                                    targetTamers.Add(target);

                                    if (newHp <= 0)
                                    {
                                        clientToModify?.Die();

                                    }
                                }
                            }

                            if (!targetTamers.Any())
                            {
                                ChaseTarget(mob);
                            }
                            else
                            {
                                // TODO: Fornecer tempo de espera da skill.

                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillVisualPacket(mob.GeneralHandler, targetSkill.SkillId).Serialize());
                                BroadcastForTargetTamers(mob.TamersViewing, new MonsterSkillDamagePacket(mob.GeneralHandler, targetSkill.SkillId, finalDamage, targetTamers).Serialize());

                                Task.Run(() =>
                                {
                                    Thread.Sleep((int)targetSkill.AnimationDelay);
                                });


                            }
                        }
                    }
                    break;
            }

            mob.SetSkillCooldown(targetSkill.Cooldown);
            mob.UpdateLastSkill();
            mob.UpdateLastSkillTry();

            if (!mob.Target.Alive)
            {
                mob.NextTarget();
            }

        }

        // ---------------------------------------------------------------------------------------

        public void ChaseTarget(MobConfigModel mob)
        {
            var diff = UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                mob.InitialLocation.X,
                mob.CurrentLocation.Y,
                mob.InitialLocation.Y);

            if (diff >= mob.HuntRange)
            {
                mob.GiveUp();
                return;
            }

            #region Get New Position
            var mobX = mob.CurrentLocation.X;
            var mobY = mob.CurrentLocation.Y;
            var partX = mob.Target.Location.X;
            var partY = mob.Target.Location.Y;

            var deltaX = partX - mobX;
            var deltaY = partY - mobY;

            var distance = Math.Sqrt(Math.Pow(deltaX, 2) + Math.Pow(deltaY, 2));
            var range = Math.Max(mob.ARValue, mob.Target.BaseInfo.ARValue);
            var min_distance = distance - range;
            var angle = Math.Atan2(deltaY, deltaX);

            var newX = (int)Math.Floor(mobX + min_distance * Math.Cos(angle));
            var newY = (int)Math.Floor(mobY + min_distance * Math.Sin(angle));
            #endregion

            var diffNew = (int)UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                newX,
                mob.CurrentLocation.Y,
                newY);

            int wait;

            if (mob.MSValue == 0)
            {
                wait = 1; 
            } 
            else
            {
             wait = diffNew / mob.MSValue * 1000;
            }

            mob.UpdateChaseTime(DateTime.Now.AddMilliseconds(1000 + wait));
            mob.MoveTo(newX, newY);
            BroadcastForTargetTamers(mob.TamersViewing, new MobRunPacket(mob).Serialize());
        }
        public void ChaseTarget(SummonMobModel mob)
        {
            var diff = UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                mob.InitialLocation.X,
                mob.CurrentLocation.Y,
                mob.InitialLocation.Y);

            if (diff >= mob.HuntRange)
            {
                mob.GiveUp();
                return;
            }

            #region Get New Position
            var mobX = mob.CurrentLocation.X;
            var mobY = mob.CurrentLocation.Y;
            var partX = mob.Target.Location.X;
            var partY = mob.Target.Location.Y;

            var deltaX = partX - mobX;
            var deltaY = partY - mobY;

            var distance = Math.Sqrt(Math.Pow(deltaX, 2) + Math.Pow(deltaY, 2));
            var range = Math.Max(mob.ARValue, mob.Target.BaseInfo.ARValue);
            var min_distance = distance - range;
            var angle = Math.Atan2(deltaY, deltaX);

            var newX = (int)Math.Floor(mobX + min_distance * Math.Cos(angle));
            var newY = (int)Math.Floor(mobY + min_distance * Math.Sin(angle));
            #endregion

            var diffNew = (int)UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                newX,
                mob.CurrentLocation.Y,
                newY);

            var wait = diffNew / mob.MSValue * 1000;
            mob.UpdateChaseTime(DateTime.Now.AddMilliseconds(1000 + wait));
            mob.MoveTo(newX, newY);
            BroadcastForTargetTamers(mob.TamersViewing, new MobRunPacket(mob).Serialize());
        }
        public void ChaseTarget(EventMobConfigModel mob)
        {
            var diff = UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                mob.InitialLocation.X,
                mob.CurrentLocation.Y,
                mob.InitialLocation.Y);

            if (diff >= mob.HuntRange)
            {
                mob.GiveUp();
                return;
            }

            #region Get New Position
            var mobX = mob.CurrentLocation.X;
            var mobY = mob.CurrentLocation.Y;
            var partX = mob.Target.Location.X;
            var partY = mob.Target.Location.Y;

            var deltaX = partX - mobX;
            var deltaY = partY - mobY;

            var distance = Math.Sqrt(Math.Pow(deltaX, 2) + Math.Pow(deltaY, 2));
            var range = Math.Max(mob.ARValue, mob.Target.BaseInfo.ARValue);
            var min_distance = distance - range;
            var angle = Math.Atan2(deltaY, deltaX);

            var newX = (int)Math.Floor(mobX + min_distance * Math.Cos(angle));
            var newY = (int)Math.Floor(mobY + min_distance * Math.Sin(angle));
            #endregion

            var diffNew = (int)UtilitiesFunctions.CalculateDistance(
                mob.CurrentLocation.X,
                newX,
                mob.CurrentLocation.Y,
                newY);

            var wait = diffNew / mob.MSValue * 1000;
            mob.UpdateChaseTime(DateTime.Now.AddMilliseconds(1000 + wait));
            mob.MoveTo(newX, newY);
            BroadcastForTargetTamers(mob.TamersViewing, new MobRunPacket(mob).Serialize());
        }

        // ---------------------------------------------------------------------------------------

        public void AttackNearbyTamer(MobConfigModel mob, List<long> nearbyTamers, List<NpcColiseumAssetModel> npcAsset)
        {
            if (mob.ReactionType == DigimonReactionTypeEnum.Agressive && !mob.Dead && !mob.InBattle && DateTime.Now > mob.AgressiveCheckTime && nearbyTamers.Any())
            {
                foreach (var tamerId in nearbyTamers)
                {
                    var targetClient = Clients.FirstOrDefault(x => x.TamerId == tamerId);
                    if (targetClient == null || targetClient.Tamer.Hidden || targetClient.Tamer.Dead || targetClient.Tamer.Riding)
                        continue;

                    var diff = UtilitiesFunctions.CalculateDistance(
                        targetClient.Tamer.Partner.Location.X,
                        mob.CurrentLocation.X,
                        targetClient.Tamer.Partner.Location.Y,
                        mob.CurrentLocation.Y);

                    if (diff <= mob.ViewRange)
                    {
                        BroadcastForTargetTamers(mob.TamersViewing, new MobTinklePacket(mob.GeneralHandler).Serialize());
                        BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(mob.GeneralHandler).Serialize());
                        BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(targetClient.Partner.GeneralHandler).Serialize());

                        mob.StartBattle(targetClient.Tamer);
                        targetClient.Tamer.StartBattle(mob);

                        // IMPORTANTE: usar versión correcta
                        if (diff <= mob.ARValue)
                        {
                            if (!targetClient.Tamer.GodMode)
                                AttackTarget(mob, npcAsset);
                        }
                        else
                        {
                            ChaseTarget(mob);
                        }

                        break;
                    }
                }
            }
        }

            public void AttackNearbyTamer(SummonMobModel mob, List<long> nearbyTamers, List<NpcColiseumAssetModel> npcAsset)
            {
                Console.WriteLine($"[SummonMob] Checking: MobId={mob.Id} Dead={mob.Dead} InBattle={mob.InBattle} ReactionType={mob.ReactionType} AggroTime={mob.AgressiveCheckTime} Now={DateTime.Now} NearbyTamers={nearbyTamers.Count}");

                if (mob.ReactionType == DigimonReactionTypeEnum.Agressive && !mob.Dead && !mob.InBattle && DateTime.Now > mob.AgressiveCheckTime && nearbyTamers.Any())
                {
                    foreach (var tamerId in nearbyTamers)
                    {
                        var targetClient = Clients.FirstOrDefault(x => x.TamerId == tamerId);
                        if (targetClient == null || targetClient.Tamer.Hidden || targetClient.Tamer.Dead || targetClient.Tamer.Riding)
                        {
                            Console.WriteLine($"[SummonMob] REJECT: TamerId={tamerId} Null={targetClient == null} Hidden={targetClient?.Tamer.Hidden} Dead={targetClient?.Tamer.Dead} Riding={targetClient?.Tamer.Riding}");
                            continue;
                        }

                        var diff = UtilitiesFunctions.CalculateDistance(
                            targetClient.Tamer.Partner.Location.X,
                            mob.CurrentLocation.X,
                            targetClient.Tamer.Partner.Location.Y,
                            mob.CurrentLocation.Y);

                        Console.WriteLine($"[SummonMob] Diff: TamerId={tamerId} Distance={diff} ViewRange={mob.ViewRange} ARValue={mob.ARValue}");

                        if (diff <= mob.ViewRange)
                        {
                            Console.WriteLine($"[SummonMob] START BATTLE: MobId={mob.Id} TamerId={tamerId}");
                            BroadcastForTargetTamers(mob.TamersViewing, new MobTinklePacket(mob.GeneralHandler).Serialize());
                            BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(mob.GeneralHandler).Serialize());
                            BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(targetClient.Partner.GeneralHandler).Serialize());

                            mob.StartBattle(targetClient.Tamer);
                            targetClient.Tamer.StartBattle(mob);

                            if (diff <= mob.ARValue)
                            {
                                Console.WriteLine($"[SummonMob] ATTACK: MobId={mob.Id} TamerId={tamerId}");
                                if (!targetClient.Tamer.GodMode)
                                    AttackTarget(mob);
                            }
                            else
                            {
                                Console.WriteLine($"[SummonMob] CHASE: MobId={mob.Id} TamerId={tamerId}");
                                ChaseTarget(mob);
                            }

                            break;
                        }
                        else
                        {
                            Console.WriteLine($"[SummonMob] OUT OF RANGE: MobId={mob.Id} TamerId={tamerId} Diff={diff}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[SummonMob] FAIL CONDITIONS: MobId={mob.Id}");
                }
            }


        public void AttackNearbyTamer(EventMobConfigModel mob, List<long> nearbyTamers, List<NpcColiseumAssetModel> npcAsset)
        {
            Console.WriteLine($"[EventMob] Checking: MobId={mob.Id} Dead={mob.Dead} InBattle={mob.InBattle} ReactionType={mob.ReactionType} AggroTime={mob.AgressiveCheckTime} Now={DateTime.Now} NearbyTamers={nearbyTamers.Count}");

            if (mob.ReactionType == DigimonReactionTypeEnum.Agressive && !mob.Dead && !mob.InBattle && DateTime.Now > mob.AgressiveCheckTime && nearbyTamers.Any())
            {
                foreach (var tamerId in nearbyTamers)
                {
                    var targetClient = Clients.FirstOrDefault(x => x.TamerId == tamerId);
                    if (targetClient == null || targetClient.Tamer.Hidden || targetClient.Tamer.Dead || targetClient.Tamer.Riding)
                    {
                        Console.WriteLine($"[EventMob] REJECT: TamerId={tamerId} Null={targetClient == null} Hidden={targetClient?.Tamer.Hidden} Dead={targetClient?.Tamer.Dead} Riding={targetClient?.Tamer.Riding}");
                        continue;
                    }

                    var diff = UtilitiesFunctions.CalculateDistance(
                        targetClient.Tamer.Partner.Location.X,
                        mob.CurrentLocation.X,
                        targetClient.Tamer.Partner.Location.Y,
                        mob.CurrentLocation.Y);

                    Console.WriteLine($"[EventMob] Diff: TamerId={tamerId} Distance={diff} ViewRange={mob.ViewRange} ARValue={mob.ARValue}");

                    if (diff <= mob.ViewRange)
                    {
                        Console.WriteLine($"[EventMob] START BATTLE: MobId={mob.Id} TamerId={tamerId}");
                        BroadcastForTargetTamers(mob.TamersViewing, new MobTinklePacket(mob.GeneralHandler).Serialize());
                        BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(mob.GeneralHandler).Serialize());
                        BroadcastForTargetTamers(mob.TamersViewing, new SetCombatOnPacket(targetClient.Partner.GeneralHandler).Serialize());

                        mob.StartBattle(targetClient.Tamer);
                        targetClient.Tamer.StartBattle(mob);

                        if (diff <= mob.ARValue)
                        {
                            Console.WriteLine($"[EventMob] ATTACK: MobId={mob.Id} TamerId={tamerId}");
                            if (!targetClient.Tamer.GodMode)
                                AttackTarget(mob);
                        }
                        else
                        {
                            Console.WriteLine($"[EventMob] CHASE: MobId={mob.Id} TamerId={tamerId}");
                            ChaseTarget(mob);
                        }

                        break;
                    }
                    else
                    {
                        Console.WriteLine($"[EventMob] OUT OF RANGE: MobId={mob.Id} TamerId={tamerId} Diff={diff}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[EventMob] FAIL CONDITIONS: MobId={mob.Id}");
            }
        }


        #region Handler
        private bool NeedNewHandler(MobConfigModel mob)
        {
            return MobHandlers.Values.FirstOrDefault(x => x == mob.Id) == 0;
        }

        public void SetMobHandler(MobConfigModel mob)
        {
            short handler = 0;

            lock (MobHandlers)
            {
                FreeMobHandler(mob.Id);

                handler = MobHandlers.First(x => x.Value == 0).Key;

                MobHandlers[handler] = mob.Id;
            }

            mob.SetHandlerValue(handler);
        }

        private bool NeedNewHandler(SummonMobModel mob)
        {
            return MobHandlers.Values.FirstOrDefault(x => x == mob.Id) == 0;
        }

        public void SetMobHandler(SummonMobModel mob)
        {
            short handler = 0;

            lock (MobHandlers)
            {
                FreeMobHandler(mob.Id);

                handler = MobHandlers.First(x => x.Value == 0).Key;

                MobHandlers[handler] = mob.Id;
            }

            mob.SetHandlerValue(handler);
        }

        public void FreeMobHandler(long mobId)
        {
            lock (MobHandlers)
            {
                var handler = MobHandlers.FirstOrDefault(x => x.Value == mobId).Key;

                if (handler > 0)
                    MobHandlers[handler] = 0;
            }
        }
        #endregion

        #region View
        private void ResetView(long tamerTarget)
        {
            lock (MobsView)
            {
                var mobViewList = MobsView.Where(x => x.Value.Contains(tamerTarget));

                foreach (var mobView in mobViewList)
                {
                    mobView.Value.Remove(tamerTarget);
                }
            }
        }
        #endregion

        // ---------------------------------------------------------------------------------------

        public void RemoveMob(MobConfigModel mob)
        {
            lock (Mobs)
            {
                if (Mobs.Contains(mob))
                {
                    FreeMobHandler(mob.Id);
                    Mobs.Remove(mob);
                }
            }
        }

        public void AddMob(MobConfigModel mob)
        {
            lock (Mobs)
            {
                if (!Mobs.Exists(x => x.Id == mob.Id))
                {
                    if (NeedNewHandler(mob))
                        SetMobHandler(mob);

                    mob.SetInitialLocation();
                    mob.UpdateCurrentHp(mob.HPValue);

                    Mobs.Add(mob);
                }
            }
        }

        public void RemoveMob(SummonMobModel mob)
        {
            lock (SummonMobs)
            {
                if (SummonMobs.Contains(mob))
                {
                    FreeMobHandler(mob.Id);
                    SummonMobs.Remove(mob);
                }
            }
        }

        public void AddMob(SummonMobModel mob)
        {
            lock (SummonMobs)
            {
                if (!SummonMobs.Exists(x => x.Id == mob.Id))
                {
                    if (NeedNewHandler(mob))
                        SetMobHandler(mob);

                    mob.SetInitialLocation();
                    mob.UpdateCurrentHp(mob.HPValue);

                    SummonMobs.Add(mob);
                }
            }
        }

        public void AddMobSumon(SummonMobModel mob)
        {
            if (NeedNewHandler(mob))
                SetMobHandler(mob);

            mob.SetInitialLocation();
            mob.UpdateCurrentHp(mob.HPValue);

            SummonMobs.Add(mob);
        }

        // ---------------------------------------------------------------------------------------

        public void UpdateMobsList()
        {
            //_mobsToAdd.Clear();
            //if (MobsToAdd != null)
            //    _mobsToAdd.AddRange(MobsToAdd);

            _mobsToDestroy.Clear();
            if (MobsToRemove != null)
                _mobsToDestroy.AddRange(MobsToRemove);
        }

        // ---------------------------------------------------------------------------------------

        private static int CalculateMobDamage(MobConfigModel? mob, out double critBonusMultiplier, out bool blocked)
        {
            var baseDamage = mob.ATValue - mob.Target.DE;

            if (baseDamage <= 0) baseDamage = 1;

            // ----------------------------------------------------------

            critBonusMultiplier = 0.00;
            double critChance = mob.CTValue / 100;

            if (critChance >= UtilitiesFunctions.RandomInt(100))
            {
                var critDamageMultiplier = 0.01;
                critBonusMultiplier = baseDamage * critDamageMultiplier;
            }

            // ----------------------------------------------------------

            blocked = mob.Target.BL >= UtilitiesFunctions.RandomDouble();

            // Level
            var levelBonusMultiplier = mob.Level > mob.Target.Level ? (0.01f * (mob.Level - mob.Target.Level)) : 0;

            // Attribute
            var attributeMultiplier = 0.00;
            if (mob.Attribute.HasAttributeAdvantage(mob.Target.BaseInfo.Attribute))
                attributeMultiplier = 0.25;
            else if (mob.Target.BaseInfo.Attribute.HasAttributeAdvantage(mob.Attribute))
                attributeMultiplier = -0.25;

            // Element
            var elementMultiplier = 0.00;
            if (mob.Element.HasElementAdvantage(mob.Target.BaseInfo.Element))
                elementMultiplier = 0.25;
            else if (mob.Target.BaseInfo.Element.HasElementAdvantage(mob.Element))
                elementMultiplier = -0.25;

            baseDamage /= blocked ? 2 : 1;

            var finalDmg = (int)Math.Floor(baseDamage + critBonusMultiplier +
                (baseDamage * levelBonusMultiplier) + (baseDamage * attributeMultiplier) + (baseDamage * elementMultiplier));

            return finalDmg;
        }

        // ---------------------------------------------------------------------------------------
    }
}
