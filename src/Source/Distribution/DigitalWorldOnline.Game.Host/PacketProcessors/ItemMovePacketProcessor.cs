using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemMovePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.MoveItem;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly SemaphoreSlim _moveItemLock = new(1, 1);


        public ItemMovePacketProcessor(MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer,
            ISender sender, ILogger logger, IMapper mapper)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var originSlot = packet.ReadShort();
            var destinationSlot = packet.ReadShort();

            //_logger.Information($"originSlot: {originSlot} | destinationSlot: {destinationSlot}");

            var itemListMovimentation = UtilitiesFunctions.SwitchItemList(originSlot, destinationSlot);

            var success = await SwapItems(client, originSlot, destinationSlot, itemListMovimentation);

            //_logger.Information($"succes: {success}");

            if (success)
            {
                switch (itemListMovimentation)
                {
                    case ItemListMovimentationEnum.InventoryToInventory:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }
                        break;

                    case ItemListMovimentationEnum.EquipmentToInventory:
                    case ItemListMovimentationEnum.InventoryToEquipment:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.Equipment.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Equipment));
                        }
                        break;

                    case ItemListMovimentationEnum.InventoryToDigivice:
                    case ItemListMovimentationEnum.DigiviceToInventory:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.Digivice.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Digivice));
                        }
                        break;

                    case ItemListMovimentationEnum.InventoryToChipset:
                    case ItemListMovimentationEnum.ChipsetToInventory:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.ChipSets.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.ChipSets));
                        }
                        break;

                    case ItemListMovimentationEnum.WarehouseToInventory:
                        {
                            client.Tamer.Warehouse.CheckEmptyItems();
                            client.Tamer.Inventory.CheckEmptyItems();

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }
                        break;
                    case ItemListMovimentationEnum.InventoryToWarehouse:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                        }
                        break;

                    case ItemListMovimentationEnum.AccountWarehouseToInventory:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Inventory.CheckEmptyItems();

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }
                        break;
                    case ItemListMovimentationEnum.InventoryToAccountWarehouse:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));

                            //_logger.Information($"InventoryToAccountWarehouse --> switch ok");
                        }
                        break;

                    case ItemListMovimentationEnum.AccountWarehouseToWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;
                    case ItemListMovimentationEnum.WarehouseToAccountWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;

                    case ItemListMovimentationEnum.AccountWarehouseToAccountWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;

                    case ItemListMovimentationEnum.WarehouseToWarehouse:
                        {
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                        }
                        break;
                }

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemMoveSuccessPacket(originSlot, destinationSlot).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );

                if (originSlot == GeneralSizeEnum.XaiSlot.GetHashCode())
                {
                    client.Tamer.Xai.RemoveXai();
                    client.Send(new XaiInfoPacket());
                    client.Send(new TamerXaiResourcesPacket(0, (short)client.Tamer.XGauge));
                    await _sender.Send(new UpdateCharacterXaiCommand(client.Tamer.Xai));
                }

                if (destinationSlot == GeneralSizeEnum.XaiSlot.GetHashCode())
                {
                    var ItemId = client.Tamer.Equipment.FindItemBySlot(destinationSlot - 1000).ItemId;

                    var XaiInfo = _mapper.Map<XaiAssetModel>(await _sender.Send(new XaiInformationQuery(ItemId)));

                    client.Tamer.Xai.EquipXai(XaiInfo.ItemId, XaiInfo.XGauge, XaiInfo.XCrystals);

                    client.Send(new XaiInfoPacket(client.Tamer.Xai));
                    client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));

                    await _sender.Send(new UpdateCharacterXaiCommand(client.Tamer.Xai));
                }

                //_logger.Verbose($"Character {client.TamerId} moved an item from {originSlot} to {destinationSlot}.");
            }
            else
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemMoveFailPacket(originSlot, destinationSlot).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                //_logger.Warning($"Character {client.TamerId} failled to move item from {originSlot} to {destinationSlot}.");
            }
        }

        private async Task<bool> SwapItems(GameClient client, short originSlot, short destinationSlot, ItemListMovimentationEnum itemListMovimentation)
        {
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (itemListMovimentation)
            {
              case ItemListMovimentationEnum.InventoryToInventory:
                await client.MoveItemLock.WaitAsync();
                try
                {
                    var result = client.Tamer.Inventory.MoveItem(originSlot, destinationSlot);
                    await GameLogger.LogInfo(
                        $"[ItemMove] {client.Tamer.Name} moved item from slot {originSlot} to {destinationSlot} in Inventory. Success: {result}",
                        "item_move");
                    return result;
                }
                finally
                {
                    client.MoveItemLock.Release();
                }                
              case ItemListMovimentationEnum.InventoryToDigivice:
            {
                await client.MoveItemLock.WaitAsync();
                try
                {
                    var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                    var dstSlot = destinationSlot - GeneralSizeEnum.DigiviceSlot.GetHashCode();

                    var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                    var destItem = client.Tamer.Digivice.FindItemBySlot(dstSlot);

                    
                     if (sourceItem == null || sourceItem.ItemId <= 0)
                     {
                         await GameLogger.LogError($"[ItemMove] Invalid source item in slot {srcSlot}.", "item_move");
                         return false;
                     }

                    if (destItem.ItemId > 0)
                    {
                        var tempItem = (ItemModel)destItem.Clone();
                        tempItem.SetItemInfo(destItem.ItemInfo);

                        client.Tamer.Digivice.AddItemWithSlot(sourceItem, dstSlot);
                        client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                        }
                    }
                    else
                    {
                        client.Tamer.Digivice.AddItemWithSlot(sourceItem, dstSlot);
                        sourceItem.SetItemId();

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, sourceItem, 1).Serialize());
                                break;
                        }
                    }

                    client.Send(new UpdateStatusPacket(client.Tamer));

                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;
                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;
                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;
                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;
                    }

                    return true;
                }
                finally
                {
                    client.MoveItemLock.Release();
                }
            }
            case ItemListMovimentationEnum.ChipsetToInventory:
            {
                var srcSlot = originSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();
                var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                var sourceItem = client.Tamer.ChipSets.FindItemBySlot(srcSlot);
                var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                if (destItem.ItemId > 0)
                {
                    var tempItem = (ItemModel)destItem.Clone();
                    tempItem.SetItemInfo(destItem.ItemInfo);

                    client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                    client.Tamer.ChipSets.AddItemWithSlot(tempItem, srcSlot);

                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                            break;
                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                            break;
                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                            break;
                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                            break;
                    }
                }
                else
                {
                    client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                    sourceItem.SetItemId();

                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                            break;
                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                            break;
                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                            break;
                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                            break;
                    }
                }

                client.Send(new UpdateStatusPacket(client.Tamer));

                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                        break;
                }

                return true;
            }

                case ItemListMovimentationEnum.InventoryToChipset:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.ChipSets.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.ChipSets.AddItemWithSlot(sourceItem, dstSlot);

                            client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;
                            }
                        }
                        else
                        {
                            client.Tamer.ChipSets.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            0).Serialize());
                                    break;

                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            0).Serialize());
                                    break;

                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            0).Serialize());
                                    break;

                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            0).Serialize());
                                    break;
                            }
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }

                    case ItemListMovimentationEnum.InventoryToEquipment:
                    {
                        await client.MoveItemLock.WaitAsync();
                        try
                        {
                            var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                            var dstSlot = destinationSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                                ? 11
                                : destinationSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();

                            var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                            var destItem = client.Tamer.Equipment.FindItemBySlot(dstSlot);

                            if (destItem.ItemId > 0)
                            {
                                var tempItem = (ItemModel)destItem.Clone();
                                tempItem.SetItemInfo(destItem.ItemInfo);

                                client.Tamer.Equipment.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                                switch (mapConfig?.Type)
                                {
                                    case MapTypeEnum.Dungeon:
                                        _dungeonServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    case MapTypeEnum.Event:
                                        _eventServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    case MapTypeEnum.Pvp:
                                        _pvpServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    default:
                                        _mapServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                }
                            }
                            else
                            {
                                client.Tamer.Equipment.AddItemWithSlot(sourceItem, dstSlot);
                                sourceItem.SetItemId();

                                switch (mapConfig?.Type)
                                {
                                    case MapTypeEnum.Dungeon:
                                        _dungeonServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    case MapTypeEnum.Event:
                                        _eventServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    case MapTypeEnum.Pvp:
                                        _pvpServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                    default:
                                        _mapServer.BroadcastForTamerViewsAndSelf(
                                            client.TamerId,
                                            new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, destItem, 1).Serialize());
                                        break;
                                }
                            }

                            client.Send(new UpdateStatusPacket(client.Tamer));

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                    break;
                            }

                            
                            if (client.Tamer.HasXai)
                            {
                                var xai = await _sender.Send(new XaiInformationQuery(client.Tamer.Xai?.ItemId ?? 0));
                                client.Tamer.SetXai(_mapper.Map<CharacterXaiModel>(xai));
                            
                                client.Send(new XaiInfoPacket(client.Tamer.Xai));
                            
                                client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));
                            }

                            return true;
                        }
                        finally
                        {
                            client.MoveItemLock.Release();
                        }
                    }
                  case ItemListMovimentationEnum.InventoryToWarehouse:
                {
                    var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                    var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        if (sourceItem == null || sourceItem.ItemId <= 0 || sourceItem.Amount <= 0)
                        {
                            await GameLogger.LogError(
                                $"[ItemMove] {client.Tamer.Name} inconsistency: invalid source item in slot {srcSlot}.",
                                "item_move");
                            return false;
                        }

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                // Captura antes de modificar
                                var movedId = sourceItem.ItemId;
                                var movedAmount = sourceItem.Amount;

                                destItem.IncreaseAmount(movedAmount);
                                sourceItem.ReduceAmount(movedAmount);

                                await GameLogger.LogInfo(
                                    $"[ItemMove] {client.Tamer.Name} stacked {movedId} x{movedAmount} from inv:{srcSlot} to warehouse:{dstSlot}.",
                                    "item_move");
                            }
                            else
                            {
                                // Captura antes
                                var swappedSrcId = sourceItem.ItemId;
                                var swappedDstId = destItem.ItemId;

                                client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                                await GameLogger.LogInfo(
                                    $"[ItemMove] {client.Tamer.Name} swapped inv:{srcSlot} ({swappedSrcId}) with warehouse:{dstSlot} ({swappedDstId}).",
                                    "item_move");
                            }
                        }
                        else
                        {
                            // Captura antes
                            var movedId = sourceItem.ItemId;

                            client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            await GameLogger.LogInfo(
                                $"[ItemMove] {client.Tamer.Name} moved {movedId} to empty warehouse:{dstSlot}.",
                                "item_move");
                        }

                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }


               case ItemListMovimentationEnum.InventoryToAccountWarehouse:
                {
                    var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                    var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                    var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                    var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                    await GameLogger.LogInfo(
                        $"[ItemMove] {client.Tamer.Name} -> InventoryToAccountWarehouse: src={srcSlot} dst={dstSlot} srcItem={sourceItem?.ItemId} amount={sourceItem?.Amount}",
                        "item_move");

                    await _moveItemLock.WaitAsync();
                    try
                    {
                        sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (sourceItem == null || sourceItem.ItemId <= 0 || sourceItem.Amount <= 0)
                        {
                            await GameLogger.LogError(
                                $"[ItemMove] {client.Tamer.Name} inconsistency: invalid source item in slot {srcSlot}. Possible desync or double-send.",
                                "item_move");
                            return false;
                        }

                        if (sourceItem.ItemInfo?.BoundType == 2)
                        {
                            await GameLogger.LogWarning(
                                $"[ItemMove] {client.Tamer.Name} tried to move BoundType=2 item to AccountWarehouse. Operation aborted.",
                                "item_move");
                            return false;
                        }

                        if (sourceItem.ItemInfo?.BoundType == 1 && sourceItem.Power > 0)
                        {
                            await GameLogger.LogWarning(
                                $"[ItemMove] {client.Tamer.Name} tried to move BoundType=1 item with Power>0 to AccountWarehouse. Operation aborted.",
                                "item_move");
                            return false;
                        }

                        if (destItem != null && destItem.ItemId > 0)
                        {
                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                var movedId = sourceItem.ItemId;
                                var movedAmount = sourceItem.Amount;

                                destItem.IncreaseAmount(movedAmount);
                                sourceItem.ReduceAmount(movedAmount);

                                await GameLogger.LogInfo(
                                    $"[ItemMove] {client.Tamer.Name} stacked {movedId} x{movedAmount} from inv:{srcSlot} -> accountWarehouse:{dstSlot}.",
                                    "item_move");
                            }
                            else
                            {
                                var tempItem = (ItemModel)destItem.Clone();
                                tempItem.SetItemInfo(destItem.ItemInfo);

                                var srcId = sourceItem.ItemId;
                                var dstId = destItem.ItemId;

                                client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                                await GameLogger.LogInfo(
                                    $"[ItemMove] {client.Tamer.Name} swapped inv:{srcSlot} ({srcId}) <-> accountWarehouse:{dstSlot} ({dstId}).",
                                    "item_move");
                            }
                        }
                        else
                        {
                            var movedId = sourceItem.ItemId;

                            client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            await GameLogger.LogInfo(
                                $"[ItemMove] {client.Tamer.Name} moved {movedId} to empty accountWarehouse:{dstSlot}.",
                                "item_move");
                        }

                        return true;
                    }
                    finally
                    {
                        _moveItemLock.Release();
                    }
                }


                case ItemListMovimentationEnum.EquipmentToInventory:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var srcSlot = originSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                            ? 11
                            : originSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();
                        var dstSlot = destinationSlot;

                        var sourceItem = client.Tamer.Equipment.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Equipment.AddItemWithSlot(tempItem, srcSlot);

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, destItem, 1).Serialize());
                                    break;
                            }
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, new ItemModel(), 0).Serialize());
                                    break;
                            }
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }

                case ItemListMovimentationEnum.DigiviceToInventory:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var srcSlot = 0;
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Digivice.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        
                         if (sourceItem == null || sourceItem.ItemId <= 0)
                         {
                             await GameLogger.LogError($"[ItemMove] Invalid source item in slot {srcSlot}.", "item_move");
                             return false;
                         }

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Digivice.AddItemWithSlot(tempItem, srcSlot);

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, destItem, 0).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, destItem, 0).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, destItem, 0).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, destItem, 0).Serialize());
                                    break;
                            }
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Digivice.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, new ItemModel(), 0).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, new ItemModel(), 0).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, new ItemModel(), 0).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, new ItemModel(), 0).Serialize());
                                    break;
                            }
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                       
                        // await GameLogger.LogInfo(
                        //     $"[ItemMove] {client.Tamer.Name} moved item from Digivice slot {srcSlot} to Inventory slot {dstSlot}.",
                        //     "item_move");
                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }

                case ItemListMovimentationEnum.WarehouseToWarehouse:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var orgSlot = (short)(originSlot - (short)GeneralSizeEnum.WarehouseMinSlot);
                        var destSlot = (short)(destinationSlot - (short)GeneralSizeEnum.WarehouseMinSlot);

                         
                        if (orgSlot < 0 || destSlot < 0)
                         {
                             await GameLogger.LogError($"[ItemMove] Invalid slots for WarehouseToWarehouse: orgSlot={orgSlot}, destSlot={destSlot}.", "item_move");
                            return false;
                         }

                        var result = client.Tamer.Warehouse.MoveItem(orgSlot, destSlot);

                        
                        // await GameLogger.LogInfo(
                        //     $"[ItemMove] {client.Tamer.Name} moved item from slot {orgSlot} to {destSlot} in Warehouse. Success: {result}",
                        //     "item_move");
                        return result;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }

                  case ItemListMovimentationEnum.AccountWarehouseToAccountWarehouse:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var orgSlot = (short)(originSlot - (short)GeneralSizeEnum.AccountWarehouseMinSlot);
                        var destSlot = (short)(destinationSlot - (short)GeneralSizeEnum.AccountWarehouseMinSlot);

                       
                         if (orgSlot < 0 || destSlot < 0)
                        {
                             await GameLogger.LogError($"[ItemMove] Invalid slots for AccountWarehouseToAccountWarehouse: orgSlot={orgSlot}, destSlot={destSlot}.", "item_move");
                            return false;
                         }

                        var result = client.Tamer.AccountWarehouse.MoveItem(orgSlot, destSlot);

                        
                        // await GameLogger.LogInfo(
                        //     $"[ItemMove] {client.Tamer.Name} moved item from slot {orgSlot} to {destSlot} in AccountWarehouse. Success: {result}",
                        //     "item_move");
                        return result;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }
                                

            case ItemListMovimentationEnum.WarehouseToInventory:
            {
                await client.MoveItemLock.WaitAsync();
                await GameLogger.LogInfo(
                    $"[Semaphore] WAIT -> TamerId={client.TamerId}",
                    "item_move");

                try
                {
                    var srcSlot = originSlot - (int)GeneralSizeEnum.WarehouseMinSlot;
                    var dstSlot = destinationSlot - (int)GeneralSizeEnum.InventoryMinSlot;

                    var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                    var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                    if (sourceItem == null || sourceItem.ItemId == 0)
                    {
                        await GameLogger.LogWarning(
                            $"[ItemMove] {client.Tamer.Name} attempted move from Warehouse slot {srcSlot} but source empty.",
                            "item_move");
                        return false;
                    }

                    if (destItem != null && destItem.ItemId > 0)
                    {
                        var tempDestItem = (ItemModel)destItem.Clone();
                        tempDestItem.SetItemInfo(destItem.ItemInfo);

                        var tempSourceItem = (ItemModel)sourceItem.Clone();
                        tempSourceItem.SetItemInfo(sourceItem.ItemInfo);

                        if (destItem.ItemId == sourceItem.ItemId)
                        {
                            if (destItem.Amount == destItem.ItemInfo.Overlap)
                            {
                                client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                client.Tamer.Warehouse.AddItemWithSlot(tempDestItem, srcSlot);

                                client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                            }
                            else
                            {
                                int remainingSpace = destItem.ItemInfo.Overlap - destItem.Amount;

                                if (remainingSpace >= sourceItem.Amount)
                                {
                                    tempDestItem.Amount += sourceItem.Amount;

                                    client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);

                                    client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                    client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                }
                                else
                                {
                                    tempSourceItem.Amount -= remainingSpace;
                                    tempDestItem.Amount += remainingSpace;

                                    client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                    client.Tamer.Warehouse.AddItemWithSlot(tempSourceItem, srcSlot);

                                    client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                    client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                }
                            }
                        }
                        else
                        {
                            client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                            client.Tamer.Warehouse.AddItemWithSlot(tempDestItem, srcSlot);

                            client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                            client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                        }
                    }
                    else
                    {
                        client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                        client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                    }

                    await GameLogger.LogInfo(
                        $"[ItemMove] SUCCESS :: {client.Tamer.Name} moved ItemId={sourceItem.ItemId} from Warehouse slot {srcSlot} to Inventory slot {dstSlot}.",
                        "item_move");

                    return true;
                }
                catch (Exception ex)
                {
                    await GameLogger.LogError(
                        $"[ItemMove] EXCEPTION :: {ex} for TamerId={client.TamerId}.",
                        "item_move");
                    return false;
                }
                finally
                {
                    client.MoveItemLock.Release();
                    await GameLogger.LogInfo(
                        $"[Semaphore] RELEASE -> TamerId={client.TamerId}",
                        "item_move");
                }
            }

                case ItemListMovimentationEnum.WarehouseToAccountWarehouse:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (sourceItem == null || sourceItem.ItemId <= 0)
                        {
                            await GameLogger.LogError($"[ItemMove] Invalid source item in slot {srcSlot}.", "item_move");
                            return false;
                        }

                        // Bloqueo Pack03: BoundType == 2
                        if (sourceItem.ItemInfo!.BoundType == 2)
                        {
                            await GameLogger.LogWarning(
                                $"[ItemMove] {client.Tamer.Name} tried to move item to AccountWarehouse with invalid Pack03 (BoundType == 2).",
                                "item_move");
                            return false;
                        }

                        // Bloqueo Pack03: BoundType == 1 + Power > 0
                        if (sourceItem.ItemInfo!.BoundType == 1 && sourceItem.Power > 0)
                        {
                            await GameLogger.LogWarning(
                                $"[ItemMove] {client.Tamer.Name} tried to move item to AccountWarehouse with invalid Pack03 (BoundType == 1, Power > 0).",
                                "item_move");
                            return false;
                        }

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                destItem.IncreaseAmount(sourceItem.Amount);
                                client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                            }
                            else
                            {
                                client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.Warehouse.AddItemWithSlot(tempItem, srcSlot);
                            }
                        }
                        else
                        {
                            client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Warehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                        }

                        await GameLogger.LogInfo(
                            $"[ItemMove] {client.Tamer.Name} moved item from Warehouse slot {srcSlot} to AccountWarehouse slot {dstSlot}.",
                            "item_move");

                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }

                case ItemListMovimentationEnum.AccountWarehouseToInventory:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        
                         if (sourceItem == null || sourceItem.ItemId <= 0)
                         {
                            await GameLogger.LogError($"[ItemMove] Invalid source item in slot {srcSlot}.", "item_move");
                             return false;
                         }

                        if (destItem.ItemId > 0)
                        {
                            var tempDestItem = (ItemModel)destItem.Clone();
                            tempDestItem.SetItemInfo(destItem.ItemInfo);

                            var tempSourceItem = (ItemModel)sourceItem.Clone();
                            tempSourceItem.SetItemInfo(sourceItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                if (destItem.Amount == destItem.ItemInfo.Overlap)
                                {
                                    client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                    client.Tamer.AccountWarehouse.AddItemWithSlot(tempDestItem, srcSlot);

                                    client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                    client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                                }
                                else
                                {
                                    int remainingSpace = destItem.ItemInfo.Overlap - destItem.Amount;

                                    if (remainingSpace >= sourceItem.Amount)
                                    {
                                        tempDestItem.Amount = tempDestItem.Amount + sourceItem.Amount;

                                        client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);

                                        client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                    }
                                    else
                                    {
                                        tempSourceItem.Amount = tempSourceItem.Amount - remainingSpace;
                                        tempDestItem.Amount = tempDestItem.Amount + remainingSpace;

                                        client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                        client.Tamer.AccountWarehouse.AddItemWithSlot(tempSourceItem, srcSlot);

                                        client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                    }
                                }
                            }
                            else
                            {
                                client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                client.Tamer.AccountWarehouse.AddItemWithSlot(tempDestItem, srcSlot);

                                client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                            }
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                        }

                       
                        // await GameLogger.LogInfo(
                        //     $"[ItemMove] {client.Tamer.Name} moved item from AccountWarehouse slot {srcSlot} to Inventory slot {dstSlot}.",
                        //     "item_move");
                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }

                case ItemListMovimentationEnum.AccountWarehouseToWarehouse:
                {
                    await client.MoveItemLock.WaitAsync();
                    try
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                         
                         if (sourceItem == null || sourceItem.ItemId <= 0)
                         {
                            await GameLogger.LogError($"[ItemMove] Invalid source item in slot {srcSlot}.", "item_move");
                             return false;
                         }

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                destItem.IncreaseAmount(sourceItem.Amount);
                                client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                            }
                            else
                            {
                                client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.AccountWarehouse.AddItemWithSlot(tempItem, srcSlot);
                            }
                        }
                        else
                        {
                            client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                        }

                     
                        // await GameLogger.LogInfo(
                        //     $"[ItemMove] {client.Tamer.Name} moved item from AccountWarehouse slot {srcSlot} to Warehouse slot {dstSlot}.",
                        //     "item_move");
                        return true;
                    }
                    finally
                    {
                        client.MoveItemLock.Release();
                    }
                }
                 }

            return false;
        }
    }
}