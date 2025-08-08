using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;
using MediatR;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Application.Separar.Commands.Delete;


namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopOpenPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopOpen;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;

        public ConsignedShopOpenPacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            ISender sender)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            await GameLogger.LogInfo($"Received ConsignedShopOpenPacket", "shops");

            var posX = packet.ReadInt();
            var posY = packet.ReadInt();
            packet.Skip(4);
            var shopName = packet.ReadString();
            packet.Skip(9);
            var sellQuantity = packet.ReadInt();

            await GameLogger.LogInfo(
                $"Player {client.Tamer.Name} tries to open shop: '{shopName}' at Map {client.Tamer.Location.MapId} ({posX},{posY}) with {sellQuantity} items",
                "shops");

            List<ItemModel> sellList = new(sellQuantity);

            for (int i = 0; i < sellQuantity; i++)
            {
                var itemId = packet.ReadInt();
                var itemAmount = packet.ReadInt();
                packet.Skip(64);
                var price = packet.ReadInt64();
                packet.Skip(8);

                var sellItem = new ItemModel(itemId, itemAmount);
                sellItem.SetSellPrice(price);

                sellList.Add(sellItem);
            }

          foreach (var item in sellList)
        {
            item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));

            var sameIdDiffPrice = sellList.Count(x => x.ItemId == item.ItemId && x.TamerShopSellPrice != item.TamerShopSellPrice);

            if (sameIdDiffPrice > 0)
            {
                await GameLogger.LogWarning(
                    $"Player {client.Tamer.Name} tried to list same itemId with different prices!",
                    "shops");

                await CloseShopAndSync(client, "You can't add the same item with different prices!");
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                return;
            }

            var alreadyListed = client.Tamer.ConsignedShopItems.CountItensById(item.ItemId);
            var availableQuantity = client.Tamer.Inventory.CountItensById(item.ItemId) - alreadyListed;

            if (item.Amount > availableQuantity)
            {
                await GameLogger.LogWarning(
                    $"Player {client.Tamer.Name} tried to list {item.Amount} of itemId {item.ItemId} but has only {availableQuantity} available (excluding already listed items). Rejecting.",
                    "shops");

                await CloseShopAndSync(client, "You're trying to list more items than you have. Please check your inventory and consigned list.");
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                return;
            }
        }


            client.Tamer.ConsignedShopItems.AddItems(sellList.Clone(), true);
            await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedShopItems));

            client.Tamer.Inventory.RemoveOrReduceItems(sellList.Clone());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            var newShop = ConsignedShop.Create(client.TamerId, shopName, posX, posY, client.Tamer.Location.MapId,
                client.Tamer.Channel, client.Tamer.ShopItemId);

            var id = await _sender.Send(new CreateConsignedShopCommand(newShop));
            newShop.SetId(id.Id);
            newShop.SetGeneralHandler(id.GeneralHandler);

            await GameLogger.LogInfo($"Shop {newShop.Id} created by {client.Tamer.Name}", "shops");

            var mapType = GetMapType(client.Tamer.Location.MapId);

            var loadPacket = new LoadConsignedShopPacket(newShop).Serialize();

            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId, loadPacket);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, loadPacket);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, loadPacket);
                    break;
                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, loadPacket);
                    break;
            }

            client.Tamer.UpdateShopItemId(0);
            client.Send(new PersonalShopPacket(TamerShopActionEnum.CloseWindow, client.Tamer.ShopItemId));
            client.Tamer.RestorePreviousCondition();

            var syncConditionPacket = new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition).Serialize();

            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncConditionPacket);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncConditionPacket);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncConditionPacket);
                    break;
                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncConditionPacket);
                    break;
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
        
        private async Task CloseShopAndSync(GameClient client, string message)
        {
            client.Send(new NoticeMessagePacket(message));
            client.Send(new PersonalShopPacket(TamerShopActionEnum.CloseWindow, 0));
            client.Tamer.RestorePreviousCondition();

            // Cerrar tienda activamente
            var shopHandler = client.Tamer.GeneralHandler;
            await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
        
            client.Send(new ConsignedShopClosePacket());

            // Sincronizar condición del Tamer
            var syncPacket = new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition).Serialize();
            var mapType = GetMapType(client.Tamer.Location.MapId);

            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncPacket);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncPacket);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncPacket);
                    break;
                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, syncPacket);
                    break;
            }
        }







    }
}
