using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HealingItemConsumePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => (GameServerPacketEnum)1002; // Placeholder, replace if needed

        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        private readonly ConcurrentDictionary<long, SemaphoreSlim> _clientLocks = new();
        private readonly ConcurrentDictionary<long, DateTime> _clientCooldowns = new();
        private readonly TimeSpan _itemCooldown = TimeSpan.FromMilliseconds(500);

        public HealingItemConsumePacketProcessor(
            StatusManager statusManager,
            MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer,
            AssetsLoader assets, ISender sender, ILogger logger, IConfiguration configuration)
        {
            _statusManager = statusManager;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _sender = sender;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var now = DateTime.UtcNow;
            if (_clientCooldowns.TryGetValue(client.TamerId, out var lastUseTime) &&
                now - lastUseTime < _itemCooldown)
            {
                _logger.Verbose($"Healing item on cooldown for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket("Please wait before using another item."));
                return;
            }

            _clientCooldowns[client.TamerId] = now;

            var clientLock = _clientLocks.GetOrAdd(client.TamerId, _ => new SemaphoreSlim(1, 1));
            await clientLock.WaitAsync();

            try
            {
                var packet = new GamePacketReader(packetData);
                packet.Skip(4);
                var itemSlot = packet.ReadShort();

                if (client.Partner == null)
                {
                    _logger.Warning($"Invalid partner for tamer id {client.TamerId}.");
                    return;
                }

                var targetItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);
                if (targetItem == null)
                {
                    _logger.Warning($"Invalid item at slot {itemSlot} for tamer id {client.TamerId}.");
                    return;
                }

                if (targetItem.Amount <= 0)
                {
                    _logger.Error($"Invalid item amount for tamer {client.TamerId}.");
                    client.Disconnect();
                    return;
                }

                var targetItemTrue = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetItem.ItemId);
                if (targetItemTrue == null)
                {
                    _logger.Warning($"Item {targetItem.ItemId} not found in assets.");
                    client.Send(new SystemMessagePacket("Invalid item data."));
                    return;
                }

                if (targetItem.ItemInfo == null || targetItem.ItemInfo.Name != targetItemTrue.Name || targetItem.ItemInfo.Id == 0)
                {
                    _logger.Error($"Item mismatch for id {targetItem.ItemId} at slot {itemSlot}.");
                    return;
                }

                if (targetItem.ItemInfo.Type != 61 && targetItem.ItemInfo.Type != 201)
                {
                    _logger.Warning($"Item type {targetItem.ItemInfo.Type} not a healing item for tamer {client.TamerId}.");
                    client.Send(new SystemMessagePacket("This item cannot be used here."));
                    return;
                }

                await ConsumeHealingItem(client, itemSlot, targetItem);

                if (now - lastUseTime > TimeSpan.FromMinutes(1))
                    _clientCooldowns.TryRemove(client.TamerId, out _);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing healing item for tamer {client.TamerId}: {ex.Message}");
                client.Send(new SystemMessagePacket("Error using item. Try again."));
            }
            finally
            {
                clientLock.Release();
            }
        }

        private async Task ConsumeHealingItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (targetItem.ItemInfo?.SkillInfo == null)
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                    new SystemMessagePacket("Invalid item data.").Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                ));
                _logger.Error($"Invalid skill info for item {targetItem.ItemId} for tamer {client.TamerId}.");
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            foreach (var apply in targetItem.ItemInfo.SkillInfo.Apply)
            {
                switch (apply.Type)
                {
                    case SkillCodeApplyTypeEnum.Percent:
                    case SkillCodeApplyTypeEnum.AlsoPercent:
                        switch (apply.Attribute)
                        {
                            case SkillCodeApplyAttributeEnum.HP:
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.HP));
                                        client.Partner.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.HP));
                                        break;
                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.HP));
                                        break;
                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.HP));
                                        break;
                                }
                                break;
                            case SkillCodeApplyAttributeEnum.DS:
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.DS));
                                        client.Partner.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.DS));
                                        break;
                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.DS));
                                        break;
                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.DS));
                                        break;
                                }
                                break;
                        }
                        break;
                    case SkillCodeApplyTypeEnum.Default:
                        switch (apply.Attribute)
                        {
                            case SkillCodeApplyAttributeEnum.HP:
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverHp(apply.Value);
                                        client.Partner.RecoverHp(apply.Value);
                                        break;
                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverHp(apply.Value);
                                        break;
                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverHp(apply.Value);
                                        break;
                                }
                                break;
                            case SkillCodeApplyAttributeEnum.DS:
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverDs(apply.Value);
                                        client.Partner.RecoverDs(apply.Value);
                                        break;
                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverDs(apply.Value);
                                        break;
                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverDs(apply.Value);
                                        break;
                                }
                                client.Send(new UpdateCurrentResourcesPacket(client.Tamer.GeneralHandler,
                                    (short)client.Tamer.CurrentHp, (short)client.Tamer.CurrentDs, (short)0));
                                client.Send(new UpdateStatusPacket(client.Tamer));

                                switch (mapConfig?.Type)
                                {
                                    case MapTypeEnum.Dungeon:
                                        _dungeonServer.BroadcastForTargetTamers(client.TamerId,
                                            UtilitiesFunctions.GroupPackets(
                                                new UpdateCurrentHPRatePacket(client.Tamer.GeneralHandler, client.Tamer.HpRate).Serialize(),
                                                new UpdateCurrentHPRatePacket(client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.HpRate).Serialize()
                                            ));
                                        break;
                                    case MapTypeEnum.Event:
                                        _eventServer.BroadcastForTargetTamers(client.TamerId,
                                            UtilitiesFunctions.GroupPackets(
                                                new UpdateCurrentHPRatePacket(client.Tamer.GeneralHandler, client.Tamer.HpRate).Serialize(),
                                                new UpdateCurrentHPRatePacket(client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.HpRate).Serialize()
                                            ));
                                        break;
                                    case MapTypeEnum.Pvp:
                                        _pvpServer.BroadcastForTargetTamers(client.TamerId,
                                            UtilitiesFunctions.GroupPackets(
                                                new UpdateCurrentHPRatePacket(client.Tamer.GeneralHandler, client.Tamer.HpRate).Serialize(),
                                                new UpdateCurrentHPRatePacket(client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.HpRate).Serialize()
                                            ));
                                        break;
                                    default:
                                        _mapServer.BroadcastForTargetTamers(client.TamerId,
                                            UtilitiesFunctions.GroupPackets(
                                                new UpdateCurrentHPRatePacket(client.Tamer.GeneralHandler, client.Tamer.HpRate).Serialize(),
                                                new UpdateCurrentHPRatePacket(client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.HpRate).Serialize()
                                            ));
                                        break;
                                }
                                break;
                        }
                        break;
                }
            }

            bool itemRemoved = client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1, itemSlot);
            if (!itemRemoved)
            {
                _logger.Error($"Failed to remove item {targetItem.ItemId} for tamer {client.TamerId}.");
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                    new SystemMessagePacket($"Failed to use {targetItem.ItemInfo.Name}.").Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                ));
                return;
            }

            try
            {
                await _sender.Send(new UpdateItemCommand(targetItem));
                await _sender.Send(new UpdateCharacterBasicInfoCommand(client.Tamer));
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                ));
                _logger.Information($"Tamer {client.Tamer.Name} used healing item {targetItem.ItemId}: {targetItem.ItemInfo.Name}");
            }
            catch (Exception ex)
            {
                client.Tamer.Inventory.AddItem(targetItem);
                _logger.Error($"Database update failed for item {targetItem.ItemId} for tamer {client.TamerId}: {ex.Message}");
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                    new SystemMessagePacket($"Error using {targetItem.ItemInfo.Name}. Try again.").Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                ));
            }
        }
    }
}