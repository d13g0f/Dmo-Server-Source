using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Models.Servers;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Game.Managers;
using Microsoft.IdentityModel.Tokens;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.Models.Account;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ComplementarInformationPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ComplementarInformation;

        private readonly PartyManager _partyManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public ComplementarInformationPacketProcessor(PartyManager partyManager, MapServer mapServer,
            DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer, AssetsLoader assets,
            ILogger logger, ISender sender, IMapper mapper)
        {
            _partyManager = partyManager;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            //_logger.Information($"Runing ComplementarInformationPacketProcessor ... **************************");

            client.Send(new SealsPacket(client.Tamer.SealList));

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            if (client.Tamer.TamerShop?.Count > 0)
            {
                _logger.Debug($"Recovering tamer shop items for character {client.TamerId}...");
                client.Tamer.Inventory.AddItems(client.Tamer.TamerShop.Items);
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Tamer.TamerShop.Clear();
                await _sender.Send(new UpdateItemsCommand(client.Tamer.TamerShop));
            }

            UpdateSkillCooldown(client);

            try
            {
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading inventory for Tamer [{client.TamerId} : {client.Tamer.Name}]\n{ex.Message}\n");
            }

            try
            {
                client.Send(new LoadInventoryPacket(client.Tamer.Warehouse, InventoryTypeEnum.Warehouse));
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading warehouse for Tamer {client.TamerId}:{client.Tamer.Name}\n{ex.Message}\n");
            }

            if (client.Tamer.AccountWarehouse != null)
            {
                client.Send(new LoadInventoryPacket(client.Tamer.AccountWarehouse, InventoryTypeEnum.AccountWarehouse));
            }

            var serverInfo = _mapper.Map<ServerObject>(await _sender.Send(new ServerByIdQuery(client.ServerId)));

            if (serverInfo.ExperienceType == 1)
            {
                client.SetServerExperience(serverInfo.Experience);
            }
            else if (serverInfo.ExperienceType == 2)
            {
                client.SetServerExperience(serverInfo.ExperienceBurn);
            }

            if (!client.DungeonMap)
            {
                client.Send(new ServerExperiencePacket(serverInfo));
            }

            if (client.MembershipExpirationDate != null)
            {
                client.Send(new MembershipPacket(client.MembershipExpirationDate.Value, client.MembershipUtcSeconds));

                var haveMBS = (client.MembershipExpirationDate.Value - DateTime.UtcNow).TotalSeconds;

                var buff = _assets.BuffInfo.Where(x => x.BuffId == 50121 || x.BuffId == 50122 || x.BuffId == 50123).ToList();

                // Have membership
                if (haveMBS > 0)
                {
                    int duration = client.MembershipUtcSecondsBuff;

                    buff.ForEach(buffAsset =>
                    {
                        if (!client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buffAsset.BuffId))
                        {
                            var newCharacterBuff = CharacterBuffModel.Create(buffAsset.BuffId, buffAsset.SkillId, 2592000, duration);

                            newCharacterBuff.SetBuffInfo(buffAsset);

                            client.Tamer.BuffList.Buffs.Add(newCharacterBuff);

                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new AddBuffPacket(client.Tamer.GeneralHandler, buffAsset, 0, duration).Serialize());
                        }

                    });

                    await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                }
                else
                {
                    // Remove Buff
                    buff.ForEach(buffAsset =>
                    {
                        if (client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buffAsset.BuffId))
                        {
                            var characterBuff = client.Tamer.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == buffAsset.BuffId);

                            client.Tamer.BuffList.Buffs.Remove(characterBuff!);

                            client.Send(new RemoveBuffPacket(client.Tamer.GeneralHandler, buffAsset.BuffId).Serialize());
                        }
                    });

                    // Remove Membership
                    client.RemoveMembership();

                    client.Send(new MembershipPacket());

                    await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                    await _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, client.MembershipExpirationDate));
                }
            }
            else
            {
                client.RemoveMembership();

                client.Send(new MembershipPacket());

                await _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, client.MembershipExpirationDate));
            }

            client.Send(new CashShopCoinsPacket(client.Premium, client.Silk));

            client.Send(new TimeRewardPacket(client.Tamer.TimeReward));

            if (client.ReceiveWelcome)
            {
                var welcomeMessages = await _sender.Send(new ActiveWelcomeMessagesAssetsQuery());

                client.Send(new WelcomeMessagePacket(welcomeMessages.PickRandom().Message));
            }

            if (client.Tamer.HasXai)
            {
                client.Send(new XaiInfoPacket(client.Tamer.Xai));
                client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));
            }

            if (!client.SentOnceDataSent)
            {
                client.Send(new TamerRelationsPacket(client.Tamer.Friends, client.Tamer.Foes));

                await _sender.Send(new UpdateCharacterInitialPacketSentOnceSentCommand(client.TamerId, true));

                if (!client.DungeonMap)
                {
                    var channels = new Dictionary<byte, byte>();

                    var mapChannels = await _sender.Send(new ChannelsByMapIdQuery(client.Tamer.Location.MapId));

                    foreach (var channel in mapChannels.OrderBy(x => x.Key))
                    {
                        channels.Add(channel.Key, channel.Value);
                    }

                    if (!channels.IsNullOrEmpty())
                    {
                        client.Send(new AvailableChannelsPacket(channels).Serialize());
                    }
                }
            }

            try
            {
                if (client.Tamer.AttendanceReward.ReedemRewards)
                {
                    CheckMonthlyReward(client);
                }

                client.Send(new TamerAttendancePacket(client.Tamer.AttendanceReward));
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to check Daily Event (Month Event):\n{ex.Message}");
            }

            client.Send(new UpdateStatusPacket(client.Tamer));
            client.Send(new UpdateMovementSpeedPacket(client.Tamer));

            client.Tamer.SetGuild(_mapper.Map<GuildModel>(await _sender.Send(new GuildByCharacterIdQuery(client.TamerId))));

            if (client.Tamer.Guild != null)
            {
                foreach (var guildMember in client.Tamer.Guild.Members)
                {
                    if (guildMember.CharacterInfo == null)
                    {
                        GameClient? guildMemberClient;
                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                guildMemberClient = _dungeonServer.FindClientByTamerId(guildMember.CharacterId);
                                break;
                            case MapTypeEnum.Event:
                                guildMemberClient = _eventServer.FindClientByTamerId(guildMember.CharacterId);
                                break;

                            case MapTypeEnum.Pvp:
                                guildMemberClient = _pvpServer.FindClientByTamerId(guildMember.CharacterId);
                                break;

                            default:
                                guildMemberClient = _mapServer.FindClientByTamerId(guildMember.CharacterId);
                                break;
                        }

                        if (guildMemberClient != null)
                        {
                            guildMember.SetCharacterInfo(guildMemberClient.Tamer);
                        }
                        else
                        {
                            guildMember.SetCharacterInfo(
                                _mapper.Map<CharacterModel>(
                                    await _sender.Send(new CharacterByIdQuery(guildMember.CharacterId))));
                        }
                    }
                }

                foreach (var guildMember in client.Tamer.Guild.Members)
                {
                    if (client.ReceiveWelcome)
                    {
                        _logger.Debug($"Sending guild information packet for character {client.TamerId}...");
                        _mapServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildInformationPacket(client.Tamer.Guild).Serialize());
                        _dungeonServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildInformationPacket(client.Tamer.Guild).Serialize());
                        _eventServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildInformationPacket(client.Tamer.Guild).Serialize());
                        _pvpServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildInformationPacket(client.Tamer.Guild).Serialize());
                    }
                }

                _logger.Debug($"Sending guild historic packet for character {client.TamerId}...");
                client.Send(new GuildInformationPacket(client.Tamer.Guild));

                _logger.Debug($"Sending guild historic packet for character {client.TamerId}...");
                client.Send(new GuildHistoricPacket(client.Tamer.Guild.Historic));
            }

            await _sender.Send(new UpdateCharacterFriendsCommand(client.Tamer, true));

            client.Tamer.Friended.ToList().ForEach(friend =>
            {
                // _logger.Information($"Sending friend connection packet for character {friend.CharacterId}...");
                _mapServer.BroadcastForUniqueTamer(friend.CharacterId,
                    new FriendConnectPacket(client.Tamer.Name).Serialize());
                _eventServer.BroadcastForUniqueTamer(friend.CharacterId,
                    new FriendConnectPacket(client.Tamer.Name).Serialize());
                _dungeonServer.BroadcastForUniqueTamer(friend.CharacterId,
                    new FriendConnectPacket(client.Tamer.Name).Serialize());
                _pvpServer.BroadcastForUniqueTamer(friend.CharacterId,
                    new FriendConnectPacket(client.Tamer.Name).Serialize());
            });

            if (client.Tamer.Guild != null)
            {
                var guildRank = await _sender.Send(new GuildCurrentRankByGuildIdQuery(client.Tamer.Guild.Id));

                if (guildRank > 0 && guildRank <= 100)
                {
                    client.Send(new GuildRankPacket(guildRank));
                }
            }

            client.Tamer.UpdateSlots();
            client.Tamer.UpdateState(CharacterStateEnum.Ready);

            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Ready));
            await _sender.Send(new UpdateAccountWelcomeFlagCommand(client.AccountId, false));

            var mapTypeConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapTypeConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    {
                    }
                    break;

                case MapTypeEnum.Event:
                    {
                        var map = _eventServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

                        if (map != null)
                            NotifyTamerKillSpawnEnteringMap(client, map);
                    }
                    break;

                case MapTypeEnum.Pvp:
                    {
                    }
                    break;

                case MapTypeEnum.Default:
                    {
                        var map = _mapServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

                        if (map != null)
                            NotifyTamerKillSpawnEnteringMap(client, map);
                    }
                    break;
            }

            var currentMap = _assets.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

            if (currentMap != null)
            {
                var characterRegion = client.Tamer.MapRegions[currentMap.RegionIndex];

                if (characterRegion != null)
                {
                    if (characterRegion.Unlocked == 0)
                    {
                        characterRegion.Unlock();

                        await _sender.Send(new UpdateCharacterMapRegionCommand(characterRegion));
                    }
                }
                else
                {
                    _logger.Error($"Unknown region index {currentMap.RegionIndex} for character {client.TamerId} at {client.TamerLocation}.");
                }
            }
            else
            {
                _logger.Error($"MapId {client.Tamer.Location.MapId} not found on [Asset.Map]");
            }

            //_logger.Information($"***********************************************************************");
        }

        private void UpdateSkillCooldown(GameClient client)
        {
            if (client.Tamer.Partner.HasActiveSkills())
            {
                foreach (var evolution in client.Tamer.Partner.Evolutions)
                {
                    foreach (var skill in evolution.Skills)
                    {
                        if (skill.Duration > 0 && skill.Expired)
                        {
                            skill.ResetCooldown();
                        }
                    }

                    _sender.Send(new UpdateEvolutionCommand(evolution));
                }

                List<int> SkillIds = new List<int>(5);
                var packetEvolution =
                    client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                if (packetEvolution != null)
                {
                    var slot = -1;

                    foreach (var item in packetEvolution.Skills)
                    {
                        slot++;

                        var skillInfo = _assets.DigimonSkillInfo.FirstOrDefault(x =>
                            x.Type == client.Partner.CurrentType && x.Slot == slot);

                        if (skillInfo != null)
                        {
                            SkillIds.Add(skillInfo.SkillId);
                        }
                    }

                    client?.Send(new SkillUpdateCooldownPacket(client.Tamer.Partner.GeneralHandler,
                        client.Tamer.Partner.CurrentType, packetEvolution, SkillIds));
                }
            }
        }

        // --------------- CHECK DAILY EVENT (MONTH EVENT)

        private async void CheckMonthlyReward(GameClient client)
        {
            var currentDate = DateTime.Now.Date;
            var lastRewardDate = client.Tamer.AttendanceReward.LastRewardDate.Date;

            try
            {
                var account = await _sender.Send(new AccountByIdQuery(client.AccountId));

                if (account == null)
                {
                    _logger.Error($"Account [{client.AccountId}] not found for Tamer {client.Tamer.Name}");
                    return;
                }
                else
                {
                    if (account.DailyRewardClaimed)
                    {
                        client.Tamer.AttendanceReward.SetRewardClaimedToday(account.DailyRewardClaimed);
                        client.Tamer.AttendanceReward.SetLastRewardDate2();
                        client.Tamer.AttendanceReward.SetTotalDays(account.DailyRewardClaimedAmount);

                        await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));

                        client.Send(new TamerAttendancePacket(client.Tamer.AttendanceReward));

                        return; // Reward already received in this account!!
                    }

                    if (client.Tamer.AttendanceReward.TotalDays != account.DailyRewardClaimedAmount)
                    {
                        client.Tamer.AttendanceReward.SetTotalDays(account.DailyRewardClaimedAmount);

                        await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                    }
                }

                if (lastRewardDate < currentDate && account != null)
                {
                    //_logger.Information($" Receiving Daily reward !!");

                    if (lastRewardDate.Month < currentDate.Month || lastRewardDate.Year < currentDate.Year)
                    {
                        client.Tamer.AttendanceReward.SetTotalDays(0);
                        client.Tamer.AttendanceReward.SetRewardClaimedToday(false);

                        account.DailyRewardClaimedAmount = 0;

                        await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                        await _sender.Send(new UpdateAccountDailyRewardCommand(account.Id, account.DailyRewardClaimed, account.DailyRewardClaimedAmount));
                    }

                    await ReedemReward(client, account);

                    if (client.Tamer.AttendanceReward.RewardClaimedToday == true)
                    {
                        client.Tamer.AttendanceReward.SetLastRewardDate();
                        client.Tamer.AttendanceReward.IncreaseTotalDays();

                        account.DailyRewardClaimed = true;
                        account.DailyRewardClaimedAmount = client.Tamer.AttendanceReward.TotalDays;

                        await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                        await _sender.Send(new UpdateAccountDailyRewardCommand(account.Id, account.DailyRewardClaimed, account.DailyRewardClaimedAmount));
                    }

                    client.Send(new TamerAttendancePacket(client.Tamer.AttendanceReward));
                    await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                }
                else
                {
                    return; // Reward already received !!
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[ERROR - CheckMonthlyReward]::{ex.Message}");
            }
        }

        private async Task ReedemReward(GameClient client, AccountDTO account)
        {
            var rewardInfo = _assets.MonthlyEvents.FirstOrDefault(x => x.CurrentDay == account.DailyRewardClaimedAmount + 1);

            if (rewardInfo != null)
            {
                var newItem = new ItemModel();
                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == rewardInfo.ItemId));

                if (newItem.ItemInfo == null)
                {
                    _logger.Warning($"No item info found with ID {rewardInfo.ItemId} for tamer {client.TamerId}.");
                    client.Send(new SystemMessagePacket($"No item info found with ID {rewardInfo.ItemId} by MonthEvent"));
                    return;
                }

                newItem.ItemId = rewardInfo.ItemId;
                newItem.Amount = rewardInfo.ItemCount;

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                var itemClone = (ItemModel)newItem.Clone();

                if (client.Tamer.Inventory.AddItem(newItem))
                {
                    client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                    //client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                    //await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));

                    client.Send(new SystemMessagePacket($"You received the item {newItem.ItemInfo.Name} in your Inventory."));

                    client.Tamer.AttendanceReward.SetRewardClaimedToday(true);

                    await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                }
                else
                {
                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                    client.Send(new SystemMessagePacket($"Your Inventory is full to receive Month Event item."));

                    client.Tamer.AttendanceReward.SetRewardClaimedToday(false);

                    await _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
                }

                client.Send(new TamerAttendancePacket(client.Tamer.AttendanceReward));
            }
            else
            {
                _logger.Error($"rewardInfo not found !!");
            }
        }

        // --------------- NOTIFY

        public void NotifyTamerKillSpawnEnteringMap(GameClient client, GameMap map)
        {
            foreach (var sourceKillSpawn in map.KillSpawns)
            {
                foreach (var mob in sourceKillSpawn.SourceMobs.Where(x => x.CurrentSourceMobRequiredAmount <= 10))
                {
                    NotifyMinimap(client, mob);
                }

                if (sourceKillSpawn.Spawn())
                {
                    NotifyMapChat(client, map, sourceKillSpawn);
                }
            }
        }

        private void NotifyMinimap(GameClient client, KillSpawnSourceMobConfigModel mob)
        {
            client.Send(new KillSpawnMinimapNotifyPacket(mob.SourceMobType, mob.CurrentSourceMobRequiredAmount)
                .Serialize());
        }

        private void NotifyMapChat(GameClient client, GameMap map, KillSpawnConfigModel sourceKillSpawn)
        {
            foreach (var targetMob in sourceKillSpawn.TargetMobs)
            {
                client.Send(new KillSpawnChatNotifyPacket(map.MapId, map.Channel, targetMob.TargetMobType).Serialize());
            }
        }
    }
}