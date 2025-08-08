using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;



namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopViewPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopView;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public ConsignedShopViewPacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            IMapper mapper,
            ISender sender)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _mapper = mapper;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            const int ExpectedPacketLength = 8;
            if (packetData.Length < ExpectedPacketLength)
            {
                await GameLogger.LogError($"[View] Invalid packet length: {packetData.Length}", "shops");
                SendUnloadShop(client, 0);
                return;
            }

            var packet = new GamePacketReader(packetData);
            packet.Skip(4);
            var handler = packet.ReadInt();

            await GameLogger.LogInfo($"[View] Player {client.Tamer.Name} requested shop handler {handler}", "shops");

            //  Obtener datos del shop
            ConsignedShop consignedShop;
            using var shopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var shopQuery = await _sender.Send(new ConsignedShopByHandlerQuery(handler), shopCts.Token);
                consignedShop = _mapper.Map<ConsignedShop>(shopQuery);
            }
            catch (OperationCanceledException)
            {
                await GameLogger.LogWarning($"[View] Timeout while querying shop {handler}.", "shops");
                SendUnloadShop(client, handler);
                return;
            }

            if (consignedShop == null)
            {
                await GameLogger.LogWarning($"[View] Shop {handler} not found. Unloading...", "shops");
                SendUnloadShop(client, handler);
                return;
            }

            //  Obtener dueño del shop
            CharacterModel shopOwner;
            using var ownerCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var ownerQuery = await _sender.Send(new CharacterAndItemsByIdQuery(consignedShop.CharacterId), ownerCts.Token);
                shopOwner = _mapper.Map<CharacterModel>(ownerQuery);
            }
            catch (OperationCanceledException)
            {
                await GameLogger.LogWarning($"[View] Timeout while retrieving owner for shop {handler}.", "shops");
                SendUnloadShop(client, handler);
                return;
            }

            if (shopOwner == null)
            {
                await GameLogger.LogWarning($"[View] Owner of shop {handler} not found. Sending close packet only to client {client.Tamer.Name}", "shops");
                client.Send(new ConsignedShopClosePacket());
                return;
            }


            //  Validar ítems
            bool itemsValid = await TryEnsureValidItems(shopOwner, 3);
            if (!itemsValid || shopOwner.ConsignedShopItems.Count == 0)
            {
                await GameLogger.LogWarning($"[View] Shop {handler} has invalid or empty items. Deleting shop.", "shops");
                await _sender.Send(new DeleteConsignedShopCommand(handler));
                SendUnloadShop(client, handler);
                return;
            }

            //  Enviar al cliente los ítems del shop
            try
            {
                await GameLogger.LogInfo($"[View] Sending shop {handler} with {shopOwner.ConsignedShopItems.Count} items to {client.Tamer.Name}.", "shops");

                client.Send(new ConsignedShopItemsViewPacket(
                    consignedShop,
                    shopOwner.ConsignedShopItems,
                    shopOwner.Name));

                await GameLogger.LogInfo($"[View] Successfully sent shop {handler} to {client.Tamer.Name}.", "shops");
            }
            catch (Exception ex)
            {
                await GameLogger.LogError($"[View] Failed to send shop {handler} to {client.Tamer.Name}: {ex.Message}", "shops");
                SendUnloadShop(client, handler);
                return;
            }

            //  TODO: ACK desde cliente
        }

        private async Task<bool> TryEnsureValidItems(CharacterModel shopOwner, int maxRetries)
        {
            int attempts = 0;

            while (attempts < maxRetries)
            {
                bool allValid = true;

                foreach (var item in shopOwner.ConsignedShopItems.Items)
                {
                    if (item.ItemInfo == null)
                        item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));

                    if (item.ItemId > 0 && item.ItemInfo == null)
                    {
                        allValid = false;
                        item.SetItemId(); // Limpia slot corrupto
                    }
                }

                if (allValid)
                    return true;

                shopOwner.ConsignedShopItems.CheckEmptyItems();
                await _sender.Send(new UpdateItemsCommand(shopOwner.ConsignedShopItems));

                await Task.Delay(100);
                attempts++;
            }

            return false;
        }

        private void SendUnloadShop(GameClient client, int handler)
        {
            try
            {
                client.Send(new ConsignedShopClosePacket());
                var unloadPacket = new UnloadConsignedShopPacket(handler).Serialize();

                short mapId = client.Tamer.Location.MapId;
                MapTypeEnum type = GetMapType(mapId);

                switch (type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId, unloadPacket);
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, unloadPacket);
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, unloadPacket);
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, unloadPacket);
                        break;
                }

                _ = GameLogger.LogInfo($"[Unload] Sent shop unload for handler {handler} to {client.Tamer.Name}", "shops");
            }
            catch (Exception ex)
            {
                GameLogger.LogError($"[Unload] Error sending unload for handler {handler} to {client.Tamer.Name}: {ex.Message}", "shops").Wait();
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



        public static void SendShopItemsToClient(GameClient buyerClient, CharacterModel seller)
        {
            if (buyerClient == null || seller == null)
                return;

            var shop = seller.ConsignedShop;
            if (shop == null)
                return;

            buyerClient.Send(new ConsignedShopItemsViewPacket(
                shop,
                seller.ConsignedShopItems,
                seller.Name));
        }
            
    }
    
    
}

