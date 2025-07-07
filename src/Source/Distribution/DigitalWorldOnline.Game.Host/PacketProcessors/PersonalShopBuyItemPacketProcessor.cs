using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Game.Security;
using MediatR;
using Serilog;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PersonalShopPurchaseItemPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TamerShopBuy;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;
        private bool hasItem = false;

        public PersonalShopPurchaseItemPacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            ILogger logger,
            IMapper mapper,
            ISender sender)
        {
            _mapServer = mapServer;
            _dungeonsServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            _logger.Information("[PersonalShopBuy]");

            //  1) Read packet
            var shopHandler = packet.ReadInt();
            var shopSlot = packet.ReadInt();
            var shopSlotInDatabase = shopSlot - 1;
            var boughtItemId = packet.ReadInt();
            var boughtAmount = packet.ReadInt();
            packet.Skip(60);
            var boughtUnitPrice = packet.ReadInt64(); // now we ignore this

            //  2) search the shop in the map
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            GameClient? personalShop = mapConfig?.Type switch
            {
                MapTypeEnum.Dungeon => _dungeonsServer.FindClientByTamerHandle(shopHandler),
                MapTypeEnum.Event => _eventServer.FindClientByTamerHandle(shopHandler),
                MapTypeEnum.Pvp => _pvpServer.FindClientByTamerHandle(shopHandler),
                _ => _mapServer.FindClientByTamerHandle(shopHandler)
            };

            if (personalShop == null)
            {
                _logger.Error($"[PersonalShop] Not found for handler: {shopHandler}");
                client.Send(new PersonalShopBuyItemPacket(TamerShopActionEnum.NoPartFound).Serialize());
                return;
            }

            //  3) validate item
            var boughtItem = personalShop.Tamer.TamerShop.Items.FirstOrDefault(x => x.Slot == shopSlotInDatabase);
            if (boughtItem == null)
            {
                _logger.Warning($"[PersonalShop] Slot {shopSlotInDatabase} not found in shop.");
                client.Send(new PersonalShopBuyItemPacket(TamerShopActionEnum.NoPartFound).Serialize());
                return;
            }

            if (boughtItem.ItemId != boughtItemId)
            {
                _logger.Warning($"[PersonalShop] ItemId mismatch: expected {boughtItem.ItemId} got {boughtItemId}");
                client.Send(new PersonalShopBuyItemPacket(TamerShopActionEnum.NoPartFound).Serialize());
                return;
            }

            if (boughtItem.Amount < boughtAmount)
            {
                _logger.Warning($"[PersonalShop] Amount exceeds stock: have {boughtItem.Amount}, tried {boughtAmount}");
                client.Send(new PersonalShopBuyItemPacket(TamerShopActionEnum.TamerShopRequest));
                return;
            }

            //  4) get real cost
            var totalCost = boughtItem.TamerShopSellPrice * boughtAmount;

            //  5) remove bits from db
            var repo = SingletonResolver.GetService<ICharacterCommandsRepository>();
            var spendOk = await repo.TrySpendBitsAsync(client.Tamer.Inventory.Id, totalCost);

            if (!spendOk)
            {
                await GameLogger.LogWarning(
                    $"[AntiCheat] Tamer {client.Tamer.Name} intentó gastar {totalCost} bits pero no tiene suficiente.",
                    "anti-cheat/bits");

                var banProcessor = SingletonResolver.GetService<BanForCheating>();
                var banMsg = banProcessor.BanAccountWithMessage(
                    client.AccountId,
                    client.Tamer.Name,
                    AccountBlockEnum.Permanent,
                    "Bit spoofing in Personal Shop",
                    client,
                    "If you are innocent, create a ticket."
                );

                client.SendToAll(new NoticeMessagePacket(banMsg).Serialize());
                client.Disconnect();
                return;
            }

            //  6) Reflejar bits descontados localmente
            client.Tamer.Inventory.RemoveBits(totalCost);
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory.Id, client.Tamer.Inventory.Bits));
            await GameLogger.LogInfo($"[PersonalShop] {client.Tamer.Name} pagó {totalCost} bits.", "transactions/bits");

            //  7) Pagar al vendedor (98% del total)
            var sellerGain = (totalCost * 98) / 100;
            personalShop.Tamer.Inventory.AddBits(sellerGain);
            await _sender.Send(new UpdateItemListBitsCommand(personalShop.Tamer.Inventory.Id, personalShop.Tamer.Inventory.Bits));
            await GameLogger.LogInfo($"[PersonalShop] {personalShop.Tamer.Name} recibió {sellerGain} bits.", "transactions/bits");

            //  8) Crear el objeto comprado
            var newItem = new ItemModel(boughtItem.ItemId, boughtAmount);
            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == boughtItem.ItemId));

            // 9) Remover del shop del vendedor
            personalShop.Tamer.TamerShop.RemoveOrReduceItems(((ItemModel)newItem.Clone()).GetList(), true);
            await _sender.Send(new UpdateItemsCommand(personalShop.Tamer.TamerShop));

            //  10) Verificar si hay más items: cerrar si queda vacío
            if (!personalShop.Tamer.TamerShop.Items.Any(x => x.ItemId > 0))
            {
                personalShop.Send(new NoticeMessagePacket("Your personal shop has been closed!"));
                personalShop.Tamer.UpdateCurrentCondition(ConditionEnum.Default);
                personalShop.Send(new PersonalShopPacket());

                var syncPacket = new SyncConditionPacket(personalShop.Tamer.GeneralHandler, personalShop.Tamer.CurrentCondition).Serialize();
                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonsServer.BroadcastForTamerViewsAndSelf(personalShop.TamerId, syncPacket);
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(personalShop.TamerId, syncPacket);
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(personalShop.TamerId, syncPacket);
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(personalShop.TamerId, syncPacket);
                        break;
                }
            }

            //  11) Dar item al comprador y guardar inventario
            client.Tamer.Inventory.AddItems(((ItemModel)newItem.Clone()).GetList());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            //  12) Notificar a ambos
            client.Send(new PersonalShopBuyItemPacket(TamerShopActionEnum.TamerShopWindow, shopSlot, boughtAmount).Serialize());
            personalShop.Send(new PersonalShopSellItemPacket(shopSlot, boughtAmount).Serialize());

            _logger.Information($"[PersonalShop] {client.Tamer.Name} compró {boughtAmount}x ItemId:{boughtItem.ItemId} de {personalShop.Tamer.Name} por {totalCost} bits.");
        }

    }
}