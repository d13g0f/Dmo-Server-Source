using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopPurchaseItemPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopPurchaseItem;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;
        private readonly MapServer _mapServer;

        public ConsignedShopPurchaseItemPacketProcessor(
            AssetsLoader assets,
            ILogger logger,
            IMapper mapper,
            ISender sender,
            MapServer mapServer)
        {
            _assets = assets;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
            _mapServer = mapServer;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            _logger.Debug($"[Shop] Reading purchase packet...");
            var shopHandler = packet.ReadInt();
            var shopSlot = packet.ReadInt();
            var boughtItemId = packet.ReadInt();
            var boughtAmount = packet.ReadInt();
            packet.Skip(60);
            var boughtUnitPrice = packet.ReadInt64();

            _logger.Debug($"[Shop] Purchase Request: Handler={shopHandler}, Slot={shopSlot}, ItemId={boughtItemId}, Amount={boughtAmount}, Price={boughtUnitPrice}");

            var shop = _mapper.Map<ConsignedShop>(await _sender.Send(new ConsignedShopByHandlerQuery(shopHandler)));
            if (shop == null)
            {
                _logger.Warning($"[Shop] Shop with handler {shopHandler} not found. Unloading.");
                client.Send(new UnloadConsignedShopPacket(shopHandler));
                return;
            }

            var sellerClient = client.Server.FindByTamerId(shop.CharacterId);
            CharacterModel seller;

            if (sellerClient != null && sellerClient.IsConnected)
            {
                _logger.Debug($"[Shop] Seller is online. Using sellerClient.Tamer.");
                seller = sellerClient.Tamer;
            }
            else
            {
                _logger.Debug($"[Shop] Seller is offline. Loading seller from DB.");
                seller = _mapper.Map<CharacterModel>(await _sender.Send(new CharacterAndItemsByIdQuery(shop.CharacterId)));
                if (seller == null)
                {
                    _logger.Warning($"[Shop] Seller not found. Deleting shop...");
                    await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
                    client.Send(new UnloadConsignedShopPacket(shopHandler));
                    return;
                }
            }

            if (seller.Name == client.Tamer.Name)
            {
                client.Send(new NoticeMessagePacket("You cannot buy from your own shop."));
                return;
            }

            var totalValue = boughtUnitPrice * boughtAmount;
            client.Tamer.Inventory.RemoveBits(totalValue);
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));
            _logger.Debug($"[Shop] Buyer paid {totalValue} bits.");

            var newItem = new ItemModel(boughtItemId, boughtAmount);
            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == boughtItemId));
            client.Tamer.Inventory.AddItems(((ItemModel)newItem.Clone()).GetList());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            seller.ConsignedShopItems.RemoveOrReduceItems(((ItemModel)newItem.Clone()).GetList());
            await _sender.Send(new UpdateItemsCommand(seller.ConsignedShopItems));

            if (sellerClient != null && sellerClient.IsConnected)
            {
                var itemName = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == boughtItemId)?.Name ?? "item";
                sellerClient.Send(new NoticeMessagePacket($"You sold {boughtAmount}x {itemName} to {client.Tamer.Name}."));

                seller.ConsignedWarehouse.AddBits(totalValue);
                await _sender.Send(new UpdateItemListBitsCommand(seller.ConsignedWarehouse));
            }
            else
            {
                seller.ConsignedWarehouse.AddBits(totalValue);
                await _sender.Send(new UpdateItemListBitsCommand(seller.ConsignedWarehouse));
            }

            if (seller.ConsignedShopItems.Count == 0)
            {
                _logger.Debug($"[Shop] Shop is empty. Deleting and notifying seller.");

                await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
                client.Send(new UnloadConsignedShopPacket(shopHandler));

                if (sellerClient != null && sellerClient.IsConnected)
                {
                    sellerClient.Send(new ConsignedShopClosePacket());
                    sellerClient.Tamer.RestorePreviousCondition();

                    _mapServer.BroadcastForTamerViewsAndSelf(
                        sellerClient.TamerId,
                        new SyncConditionPacket(
                            sellerClient.Tamer.GeneralHandler,
                            sellerClient.Tamer.CurrentCondition).Serialize());

                    _logger.Debug($"[Shop] Seller visual state refreshed after full sale.");
                }
            }

            client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
            client.Send(new ConsignedShopBoughtItemPacket(TamerShopActionEnum.ConsignedShopRequest, shopSlot, boughtAmount));
            client.Send(new ConsignedShopItemsViewPacket(shop, seller.ConsignedShopItems, seller.Name));
        }
    }
}
