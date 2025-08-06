using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;
using MediatR;
using Microsoft.Identity.Client;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchIncreasePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchIncrease;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ConfigsLoader _configs;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public HatchIncreasePacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            ConfigsLoader configs,
            ILogger logger,
            ISender sender)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _configs = configs;
            _logger = logger;
            _sender = sender;
        }

        // Main processing method for hatch increase packet
        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var vipEnabled = packet.ReadByte();
            var npcId = packet.ReadInt();
            var dataTier = packet.ReadByte(); // 0 = Low, 1 = Mid

            // Validate egg and configurations
            var targetItem = client.Tamer.Incubator.EggId;
            if (!ValidateEgg(targetItem, out var itemInfo, out var hatchInfo))
            {
              _ =  GameLogger.LogInfo($"Invalid egg {targetItem}: ItemInfo or HatchInfo not found.");
                return;
            }

            var hatchConfig = _configs.Hatchs.FirstOrDefault(x => x.Type.GetHashCode() == client.Tamer.Incubator.HatchLevel + 1);
            if (hatchConfig == null)
            {
                BroadcastHatchFailure(client, HatchIncreaseResultEnum.Failled);
              _ =  GameLogger.LogInfo($"Invalid hatch config for level {client.Tamer.Incubator.HatchLevel + 1}.");
                client.Send(new SystemMessagePacket($"Invalid hatch config for level {client.Tamer.Incubator.HatchLevel + 1}."));
                return;
            }

            // Process hatch attempt based on data tier
            var result = await TryHatch(client, dataTier, itemInfo, hatchInfo, hatchConfig);
            
            // Update game state
            client.Tamer.Incubator.RemoveBackupDisk();
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }

        // Validates egg and retrieves item and hatch information
        private bool ValidateEgg(int targetItem, out dynamic itemInfo, out dynamic hatchInfo)
        {
            itemInfo = null;
            hatchInfo = null;

            if (targetItem == 0)
            {
              _ =  GameLogger.LogInfo("Target egg not found.");
                return false;
            }

            itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetItem);
            if (itemInfo == null)
            {
              _ =  GameLogger.LogInfo($"ItemInfo not found for egg {targetItem}.");
                return false;
            }

            hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == targetItem);
            if (hatchInfo == null)
            {
                _logger.Warning($"Unknown hatch info for egg {targetItem}.");
                return false;
            }

            return true;
        }

                // Attempts hatch and returns the result
        private async Task<HatchIncreaseResultEnum?> TryHatch(GameClient client, byte dataTier, dynamic itemInfo, dynamic hatchInfo, dynamic hatchConfig)
        {
            var (section, amount) = dataTier == 0 
                ? (hatchInfo.LowClassDataSection, hatchInfo.LowClassDataAmount)
                : (hatchInfo.MidClassDataSection, hatchInfo.MidClassDataAmount);

            // Check and consume required items
            if (!client.Tamer.Inventory.RemoveOrReduceItemsBySection(section, amount))
            {
                HandleInvalidData(client, dataTier, hatchInfo.ItemId, section);
                return HatchIncreaseResultEnum.Failled;
            }

            // Determine if hatch is guaranteed or chance-based
            bool isGuaranteed = dataTier == 0
                ? client.Tamer.Incubator.HatchLevel < hatchInfo.MidClassBreakPoint && itemInfo.Class != 4 &&
                (hatchInfo.LowClassBreakPoint == hatchInfo.MidClassBreakPoint || hatchInfo.LowClassLimitLevel == hatchInfo.MidClassBreakPoint)
                : client.Tamer.Incubator.HatchLevel < hatchInfo.MidClassBreakPoint && itemInfo.Class != 4;

            if (isGuaranteed)
            {
                HandleHatchSuccess(client, hatchInfo.ItemId, section, amount);
                return null; // Success is indicated by packet, not enum
            }

            // Chance-based hatch
            if (RollChance(hatchConfig.SuccessChance))
            {
                HandleHatchSuccess(client, hatchInfo.ItemId, section, amount);
                return null; // Success is indicated by packet, not enum
            }

            // Handle hatch failure
            if (RollChance(hatchConfig.BreakChance))
            {
                return HandleHatchBreak(client, hatchInfo, section, amount);
            }

            BroadcastHatchFailure(client, HatchIncreaseResultEnum.Failled);
            LogHatchFailure(client.TamerId, hatchInfo.ItemId, section, amount, HatchIncreaseResultEnum.Failled, client.Tamer.Incubator.HatchLevel + 1);
            return HatchIncreaseResultEnum.Failled;
        }

        // Handles successful hatch
        private void HandleHatchSuccess(GameClient client, int itemId, int section, int amount)
        {
            client.Tamer.Incubator.IncreaseLevel();
            BroadcastHatchSuccess(client, client.Tamer.Incubator.HatchLevel);
            LogHatchSuccess(client.TamerId, itemId, client.Tamer.Incubator.HatchLevel, section, amount);
        }

        // Handles egg breaking or backup
        private HatchIncreaseResultEnum HandleHatchBreak(GameClient client, dynamic hatchInfo, int section, int amount)
        {
            if (client.Tamer.Incubator.BackupDiskId > 0)
            {
                BroadcastHatchFailure(client, HatchIncreaseResultEnum.Backuped);
                LogHatchFailure(client.TamerId, hatchInfo.ItemId, section, amount, HatchIncreaseResultEnum.Backuped, client.Tamer.Incubator.HatchLevel + 1);
                return HatchIncreaseResultEnum.Backuped;
            }

            client.Tamer.Incubator.RemoveEgg();
            BroadcastHatchFailure(client, HatchIncreaseResultEnum.Broken);
            LogHatchFailure(client.TamerId, hatchInfo.ItemId, section, amount, HatchIncreaseResultEnum.Broken, client.Tamer.Incubator.HatchLevel + 1);
            return HatchIncreaseResultEnum.Broken;
        }

        // Handles invalid data and potential ban
        private async Task HandleInvalidData(GameClient client, byte dataTier, int targetItem, int section)
        {
            // Validate client and Tamer
            if (client?.Tamer == null)
            {
                await GameLogger.LogError($"Cliente o Tamer nulo al procesar datos inválidos para EggId={targetItem}, Sección={section}", "hatch_increase");
                return;
            }

            var result = HatchIncreaseResultEnum.Failled;
            var sectionName = dataTier == 0 ? "baja" : "media";

            // Broadcast hatch failure
            BroadcastHatchFailure(client, result);

            // Log the issue in Spanish
            await GameLogger.LogWarning($"Cantidad de datos de clase {sectionName} inválida para huevo {targetItem} y sección {section} para TamerId={client.TamerId}", "hatch_increase");

            // Notify client with English message
            client.Send(new SystemMessagePacket($"Invalid {sectionName} class data amount for egg {targetItem} and section {section}."));

            // Disconnect client instead of banning
            await GameLogger.LogInfo($"Desconectando cliente TamerId={client.TamerId} debido a datos inválidos para huevo {targetItem}", "hatch_increase");
            client.Disconnect(); // Assumes a Disconnect method exists on GameClient
        }

        // Broadcasts successful hatch result
    
            private void BroadcastHatchSuccess(GameClient client, int hatchLevel)
            {
                var mapId = client?.Tamer?.Location.MapId ?? 0;
                var mapType = GetMapTypeById(mapId);
                var packet = new HatchIncreaseSucceedPacket(client.Tamer.GeneralHandler, hatchLevel).Serialize();

                switch (mapType)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                        break;
                }

                _ = GameLogger.LogInfo("Hatch", $"[BroadcastHatchSuccess] TamerId: {client.TamerId} sent HatchIncreaseSucceedPacket with level: {hatchLevel} on map {mapId} ({mapType}). Packet: {BitConverter.ToString(packet)}");
            }

        // Broadcasts failed hatch result
        private void BroadcastHatchFailure(GameClient client, HatchIncreaseResultEnum result)
        {
            var mapId = client?.Tamer?.Location.MapId ?? 0;
            var mapType = GetMapTypeById(mapId);
            var packet = new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, result).Serialize();

            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                    break;
                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
                    break;
            }

            _ = GameLogger.LogInfo("Hatch", $"[BroadcastHatchFailure] TamerId: {client.TamerId} sent HatchIncreaseFailedPacket with result: {result} on map {mapId} ({mapType}).");
        }

        // Checks if a random roll succeeds based on probability
        private bool RollChance(double probability)
        {
            return probability >= UtilitiesFunctions.RandomDouble();
        }

        // Logs failure of hatch attempt
        private void LogHatchFailure(long tamerId, int itemId, int section, int amount, HatchIncreaseResultEnum result, int targetLevel)
        {
            var message = result switch
            {
                HatchIncreaseResultEnum.Failled => "failed",
                HatchIncreaseResultEnum.Broken => "broke",
                HatchIncreaseResultEnum.Backuped => "saved by backup",
                _ => "unknown result"
            };

            _ = GameLogger.LogInfo("Hatch", $"Character {tamerId} {message} to increase egg {itemId} to level {targetLevel} with data section {section} x{amount}.");
        }

        // Logs successful hatch attempt
        private void LogHatchSuccess(long tamerId, int itemId, int level, int section, int amount)
        {
             _ = GameLogger.LogInfo("Hatch", $"Character {tamerId} increased egg {itemId} to level {level} using data section {section} x{amount}.");
        }

        // Determines map type based on map ID
        private static MapTypeEnum GetMapTypeById(short mapId)
        {
            if (GameMap.DungeonMapIds.Contains(mapId))
                return MapTypeEnum.Dungeon;

            if (GameMap.PvpMapIds.Contains(mapId))
                return MapTypeEnum.Pvp;

            if (GameMap.EventMapIds.Contains(mapId))
                return MapTypeEnum.Event;

            return MapTypeEnum.Default;
        }
    }
}