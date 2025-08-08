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
using GameServer.Logging;
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

            var shopHandler = packet.ReadInt();
            var shopSlot = packet.ReadInt();
            var shopSlotInDatabase = shopSlot - 1;
            var boughtItemId = packet.ReadInt();
            var boughtAmount = packet.ReadInt();
            packet.Skip(60);
            var boughtUnitPrice = packet.ReadInt64();

            var shop = _mapper.Map<ConsignedShop>(await _sender.Send(new ConsignedShopByHandlerQuery(shopHandler)));
            if (shop == null)
            {
                client.Send(new UnloadConsignedShopPacket(shopHandler));
                return;
            }

            var sellerClient = client.Server.FindByTamerId(shop.CharacterId);
            CharacterModel seller = sellerClient?.Tamer
                ?? _mapper.Map<CharacterModel>(await _sender.Send(new CharacterAndItemsByIdQuery(shop.CharacterId)));

            if (seller == null)
            {
                await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
                client.Send(new UnloadConsignedShopPacket(shopHandler));
                return;
            }

            if (seller.Name == client.Tamer.Name)
            {
                client.Send(new NoticeMessagePacket("You cannot buy from your own shop."));
                return;
            }

            var itemInShop = seller.ConsignedShopItems.Items.FirstOrDefault(x => x.Slot == shopSlotInDatabase);
            if (itemInShop == null || itemInShop.ItemId != boughtItemId || itemInShop.Amount < boughtAmount)
            {
                client.Send(new NoticeMessagePacket("Item not available or quantity insufficient."));
                return;
            }

            var totalValue = itemInShop.TamerShopSellPrice * boughtAmount; // Precio del servidor

            // Opcional: Verificación anti-cheat adicional
            if (boughtUnitPrice != itemInShop.TamerShopSellPrice)
            {
                _logger.Warning($"Possible cheat attempt by {client.Tamer.Name}: Price mismatch (client: {boughtUnitPrice}, server: {itemInShop.TamerShopSellPrice})");
                // Podrías registrar o sancionar aquí
            }

            if (client.Tamer.Inventory.Bits < totalValue)
            {
                client.Send(new NoticeMessagePacket("You don't have enough bits."));
                return;
            }

            // Eliminación del ítem del vendedor
            var removalSuccess = seller.ConsignedShopItems.RemoveOrReduceItems(new List<ItemModel>
            {
                new ItemModel(boughtItemId, boughtAmount)
            }, reArrangeSlots: true);

            if (!removalSuccess)
            {
                client.Send(new NoticeMessagePacket("Failed to update seller inventory."));
                return;
            }

            await _sender.Send(new UpdateItemsCommand(seller.ConsignedShopItems));

            // Restar bits al comprador
            client.Tamer.Inventory.RemoveBits(totalValue);
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));

            // Agregar ítem al comprador
            var boughtItem = new ItemModel(boughtItemId, boughtAmount);
            boughtItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == boughtItemId));
            client.Tamer.Inventory.AddItems(boughtItem.GetList());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            // Agregar bits al vendedor
            seller.ConsignedWarehouse.AddBits(totalValue);
            await _sender.Send(new UpdateItemListBitsCommand(seller.ConsignedWarehouse));

            if (sellerClient != null && sellerClient.IsConnected)
            {
                var itemName = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == boughtItemId)?.Name ?? "item";
                sellerClient.Send(new NoticeMessagePacket($"You sold {boughtAmount}x {itemName} to {client.Tamer.Name}."));
            }

            // Actualizar vista del comprador
            client.Send(new ConsignedShopBoughtItemPacket(TamerShopActionEnum.TamerShopWindow, shopSlot, boughtAmount));

            if (seller.ConsignedShopItems.Count == 0)
            {
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
                }
                await GameLogger.LogInfo(
                $"[SALE] Buyer={client.Tamer.Name} (ID={client.Tamer.Id}) bought {boughtAmount}x {boughtItemId} ({itemInShop.ItemId}) from Seller={seller.Name} (ID={seller.Id}) at {itemInShop.TamerShopSellPrice} bits each, Total={totalValue}. BuyerBits={client.Tamer.Inventory.Bits}, SellerBits={seller.ConsignedWarehouse.Bits}",
                "shops/sales");

                return;
            }

        }
        
        

    }
}



