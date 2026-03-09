using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using Microsoft.IdentityModel.Tokens;
using MediatR;
using Serilog;
using GameServer.Logging;
using DigitalWorldOnline.Commons.Models.Map;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class InitialInformationPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.InitialInformation;

        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly PvpServer _pvpServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public InitialInformationPacketProcessor(
            PartyManager partyManager,
            StatusManager statusManager,
            MapServer mapServer,
            PvpServer pvpServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            AssetsLoader assets,
            ILogger logger, ISender sender, IMapper mapper)
        {
            _partyManager = partyManager;
            _statusManager = statusManager;
            _mapServer = mapServer;
            _pvpServer = pvpServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _assets = assets;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

           _ = GameLogger.LogInfo("[loading] Starting InitialInformationPacketProcessor", "Loading");

            packet.Skip(4);
            var accountId = packet.ReadUInt();
            var accessCode = packet.ReadUInt();

           _ = GameLogger.LogInfo($"[loading] Received accountId={accountId}, accessCode={accessCode}", "Loading");

            var account = _mapper.Map<AccountModel>(await _sender.Send(new AccountByIdQuery(accountId)));
            client.SetAccountInfo(account);

           _ = GameLogger.LogInfo($"[loading] Loaded account with ID={account.Id}", "Loading");

            try
            {
                CharacterModel? character = _mapper.Map<CharacterModel>(
                        await _sender.Send(new CharacterByIdQuery(account.LastPlayedCharacter)));

               _ = GameLogger.LogInfo($"[loading] Loaded character with ID={account.LastPlayedCharacter}", "Loading");

                if (character == null || character.Partner == null)
                {
                    _logger.Error($"Invalid character information for tamer id {account.LastPlayedCharacter}.");
                   _ = GameLogger.LogInfo($"[loading] Invalid character information for tamer id {account.LastPlayedCharacter}", "Loading");
                    return;
                }

                account.ItemList.ForEach(character.AddItemList);

               _ = GameLogger.LogInfo("[loading] Added items to character inventory", "Loading");

                foreach (var digimon in character.Digimons)
                {
                    digimon.SetTamer(character);

                    digimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(digimon.CurrentType));
                    digimon.SetBaseStatus(_statusManager.GetDigimonBaseStatus(digimon.CurrentType, digimon.Level, digimon.Size));
                    digimon.SetTitleStatus(_statusManager.GetTitleStatus(character.CurrentTitle));
                    digimon.SetSealStatus(_assets.SealInfo);
                }

               _ = GameLogger.LogInfo("[loading] Configured digimon base info and status", "Loading");

                var tamerLevelStatus = _statusManager.GetTamerLevelStatus(character.Model, character.Level);

                character.SetBaseStatus(_statusManager.GetTamerBaseStatus(character.Model));
                character.SetLevelStatus(tamerLevelStatus);

               _ = GameLogger.LogInfo("[loading] Configured tamer base and level status", "Loading");

                character.NewViewLocation(character.Location.X, character.Location.Y);
                character.NewLocation(character.Location.MapId, character.Location.X, character.Location.Y);
                character.Partner.NewLocation(character.Location.MapId, character.Location.X, character.Location.Y);

               _ = GameLogger.LogInfo("[loading] Set new location for character and partner", "Loading");

                character.RemovePartnerPassiveBuff();
                character.SetPartnerPassiveBuff();
                character.Partner.SetTamer(character);

                await _sender.Send(new UpdateDigimonBuffListCommand(character.Partner.BuffList));

               _ = GameLogger.LogInfo("[loading] Updated digimon buff list", "Loading");

                foreach (var item in character.ItemList.SelectMany(x => x.Items).Where(x => x.ItemId > 0))
                    item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item?.ItemId));

               _ = GameLogger.LogInfo("[loading] Set item info for inventory items", "Loading");

                foreach (var buff in character.BuffList.ActiveBuffs)    
                    buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x =>
                        x.SkillCode == buff.SkillId || x.DigimonSkillCode == buff.SkillId));

                foreach (var buff in character.Partner.BuffList.ActiveBuffs)
                    buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x =>
                        x.SkillCode == buff.SkillId || x.DigimonSkillCode == buff.SkillId));

               _ = GameLogger.LogInfo("[loading] Set buff info for character and partner", "Loading");

                _logger.Debug($"Getting available channels...");

                if (client.DungeonMap)
                {
                    character.SetCurrentChannel(0);
                   _ = GameLogger.LogInfo("[loading] Set current channel to 0 for dungeon map", "Loading");
                }
                else
                {
                    var channels = (Dictionary<byte, byte>)await _sender.Send(new ChannelsByMapIdQuery(character.Location.MapId));
                    byte? channel = GetTargetChannel(character.Channel, channels);

                    if (channel == null)
                    {
                        _logger.Information($"Creating new channel for map {character.Location.MapId}...");
                        channel = CreateNewChannelForMap(channels);
                       _ = GameLogger.LogInfo($"[loading] Created new channel {channel} for map {character.Location.MapId}", "Loading");
                    }

                    if (character.Channel == byte.MaxValue)
                    {
                        character.SetCurrentChannel(channel.Value);
                       _ = GameLogger.LogInfo($"[loading] Set current channel to {channel.Value}", "Loading");
                    }
                }

                character.UpdateState(CharacterStateEnum.Loading);
                client.SetCharacter(character);

                client.SetSentOnceDataSent(character.InitialPacketSentOnceSent);

                _logger.Debug($"Updating character state...");
                await _sender.Send(new UpdateCharacterStateCommand(character.Id, CharacterStateEnum.Loading));

               _ = GameLogger.LogInfo("[loading] Updated character state to Loading", "Loading");

            var mapId = client.Tamer.Location.MapId;

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(mapId));

            // ------------------------------------------------------------------------------------
            // MAP TYPE RESOLUTION (single source of truth with safe fallback)
            // DB config has priority, but if DB says Default and hardcoded list says PvP/Dungeon/Event,
            // we override to avoid routing inconsistencies.
            // ------------------------------------------------------------------------------------
            MapTypeEnum resolvedType;

            if (mapConfig == null)
            {
                resolvedType = GetMapType(mapId);

                _logger.Warning(
                    $"Adding Tamer {character.Id}:{character.Name} to map {mapId} Ch {character.Channel}... " +
                    $"(No DB MapConfig. Fallback={resolvedType})"
                );
            }
            else
            {
                resolvedType = mapConfig.Type;

                // override only when DB says Default but code says something special
                if (resolvedType == MapTypeEnum.Default)
                {
                    var fallback = GetMapType(mapId);
                    if (fallback != MapTypeEnum.Default)
                    {
                        _logger.Warning(
                            $"[MapRouting] DB MapType=Default but hardcoded says {fallback}. " +
                            $"Overriding. MapId={mapId}"
                        );
                        resolvedType = fallback;
                    }
                }
            }

            // ------------------------------------------------------------------------------------
            // ROUTE CLIENT TO THE RIGHT SERVER
            // ------------------------------------------------------------------------------------
            switch (resolvedType)
            {
                case MapTypeEnum.Dungeon:
                    _logger.Information(
                        $"Adding Tamer {character.Id}:{character.Name} to map {mapId} Ch 0... (Dungeon Map)");
                    client.Tamer.SetCurrentChannel(0);
                    await _dungeonsServer.AddClient(client);
                    break;

                case MapTypeEnum.Pvp:
                    _logger.Information(
                        $"Adding Tamer {character.Id}:{character.Name} to map {mapId} Ch {character.Channel}... (PVP Map)");
                    await _pvpServer.AddClient(client);
                    break;

                case MapTypeEnum.Event:
                    _logger.Information(
                        $"Adding Tamer {character.Id}:{character.Name} to map {mapId} Ch {character.Channel}... (Event Map)");
                    await _eventServer.AddClient(client);
                    break;

                default:
                    _logger.Information(
                        $"Adding Tamer {character.Id}:{character.Name} to map {mapId} Ch {character.Channel}... (Normal Map)");
                    await _mapServer.AddClient(client);
                    break;
            }


                while (client.Loading)
                {
                    await Task.Delay(200);
                   _ = GameLogger.LogInfo("[loading] Waiting for client loading to complete", "Loading");
                }

                character.SetGenericHandler(character.Partner.GeneralHandler);

               _ = GameLogger.LogInfo("[loading] Set generic handler for character", "Loading");

                if (!client.DungeonMap)
                {
                    var region = _assets.Maps.FirstOrDefault(x => x.MapId == character.Location.MapId);

                    if (region != null)
                    {
                        if (character.MapRegions[region.RegionIndex].Unlocked != 0x80)
                        {
                            var characterRegion = character.MapRegions[region.RegionIndex];
                            characterRegion.Unlock();

                            await _sender.Send(new UpdateCharacterMapRegionCommand(characterRegion));
                           _ = GameLogger.LogInfo($"[loading] Unlocked map region {region.RegionIndex} for character", "Loading");
                        }
                    }
                }

                client.Send(new InitialInfoPacket(character, null));
               _ = GameLogger.LogInfo("[loading] Sent InitialInfoPacket to client", "Loading");

                var party = _partyManager.FindParty(client.TamerId);

                if (party != null)
                {
                    party.UpdateMember(party[client.TamerId], character);

                    var firstMemberLocation =
                        party.Members.Values.FirstOrDefault(x => x.Location.MapId == client.Tamer.Location.MapId);

                    if (firstMemberLocation != null)
                    {
                        character.SetCurrentChannel(firstMemberLocation.Channel);
                        client.Tamer.SetCurrentChannel(firstMemberLocation.Channel);
                       _ = GameLogger.LogInfo($"[loading] Set current channel to {firstMemberLocation.Channel} based on party member", "Loading");
                    }

                    foreach (var target in party.Members.Values.Where(x => x.Id != client.TamerId))
                    {
                        var targetClient = _mapServer.FindClientByTamerId(target.Id);
                        if (targetClient == null) targetClient = _dungeonsServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) continue;

                        KeyValuePair<byte, CharacterModel> partyMember =
                            party.Members.FirstOrDefault(x => x.Value.Id == client.TamerId);

                        targetClient.Send(
                            UtilitiesFunctions.GroupPackets(
                                new PartyMemberWarpGatePacket(partyMember, targetClient.Tamer).Serialize(),
                                new PartyMemberMovimentationPacket(partyMember).Serialize()
                            ));
                       _ = GameLogger.LogInfo($"[loading] Sent party member packets to target client {targetClient.TamerId}", "Loading");
                    }
                    await Task.Delay(100);

                    client.Send(new PartyMemberListPacket(party, character.Id));
                   _ = GameLogger.LogInfo("[loading] Sent PartyMemberListPacket to client", "Loading");
                }

                await ReceiveArenaPoints(client);
               _ = GameLogger.LogInfo("[loading] Processed arena points", "Loading");

                _logger.Debug($"Send initial packet for tamer {client.Tamer.Name}");

                await _sender.Send(new ChangeTamerIdTPCommand(client.Tamer.Id, (int)0));
               _ = GameLogger.LogInfo("[loading] Updated tamer TP to 0", "Loading");

                await _sender.Send(new UpdateCharacterChannelCommand(character.Id, character.Channel));
               _ = GameLogger.LogInfo($"[loading] Updated character channel to {character.Channel}", "Loading");
            }
            catch (Exception ex)
            {
                _logger.Error($"An error occurred for Player [{account.LastPlayedCharacter}]:");
                _logger.Error($"{ex.Message}");
                _logger.Error($"Inner Stacktrace: {ex.ToString()}");
                _logger.Error($"Stacktrace: {ex.StackTrace}");
                _logger.Error($"Disconnecting Client");
                client.Disconnect();
               _ = GameLogger.LogInfo($"[loading] Exception occurred: {ex.Message}", "Loading");
            }
        }

        private static MapTypeEnum GetMapType(short mapId)
        {
            if (GameMap.DungeonMapIds.Contains(mapId))
                return MapTypeEnum.Dungeon;

            if (GameMap.PvpMapIds.Contains(mapId))
                return MapTypeEnum.Pvp;

            if (GameMap.EventMapIds.Contains(mapId))
                return MapTypeEnum.Event;

            return MapTypeEnum.Default;
        }


        private async Task ReceiveArenaPoints(GameClient client)
        {
            if (client.Tamer.Points.Amount > 0)
            {
                var newItem = new ItemModel();
                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == client.Tamer.Points.ItemId));

                newItem.ItemId = client.Tamer.Points.ItemId;
                newItem.Amount = client.Tamer.Points.Amount;

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                var itemClone = (ItemModel)newItem.Clone();

                if (client.Tamer.Inventory.AddItem(newItem))
                {
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                   _ = GameLogger.LogInfo("[loading] Added arena points item to inventory", "Loading");
                }
                else
                {
                    newItem.EndDate = DateTime.Now.AddDays(7);

                    client.Tamer.GiftWarehouse.AddItem(newItem);
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                   _ = GameLogger.LogInfo("[loading] Added arena points item to gift warehouse", "Loading");
                }

                client.Tamer.Points.SetAmount(0);
                client.Tamer.Points.SetCurrentStage(0);

                await _sender.Send(new UpdateCharacterArenaPointsCommand(client.Tamer.Points));
               _ = GameLogger.LogInfo("[loading] Updated arena points to 0", "Loading");
            }
            else if (client.Tamer.Points.CurrentStage > 0)
            {
                client.Tamer.Points.SetCurrentStage(0);
                await _sender.Send(new UpdateCharacterArenaPointsCommand(client.Tamer.Points));
               _ = GameLogger.LogInfo("[loading] Reset current stage to 0", "Loading");
            }
        }

        private byte? GetTargetChannel(byte currentChannel, Dictionary<byte, byte> channels)
        {
            if (currentChannel == byte.MaxValue && !channels.IsNullOrEmpty())
            {
                return SelectRandomChannel(channels.Keys);
            }

            return currentChannel == byte.MaxValue ? null : (byte?)currentChannel;
        }

        private byte SelectRandomChannel(IEnumerable<byte> channelKeys)
        {
            var random = new Random();
            var keys = channelKeys.ToList();
            return keys[random.Next(keys.Count)];
        }

        private byte CreateNewChannelForMap(Dictionary<byte, byte> channels)
        {
            channels.Add(channels.Keys.GetNewChannel(), 0);
            return channels
                .OrderByDescending(x => x.Value)
                .First(x => x.Value < byte.MaxValue)
                .Key;
        }
    }
}