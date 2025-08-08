using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopRetrieve;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;

        public ConsignedShopRetrievePacketProcessor(
            AssetsLoader assets,
            MapServer mapServer,
            ILogger logger,
            IMapper mapper,
            ISender sender)
        {
            _assets = assets;
            _mapServer = mapServer;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var logFolder = $"shop/retrieve";
            try
            {
                await GameLogger.LogInfo($"[START] Retrieve request received", logFolder);

                var shopDto = await _sender.Send(new ConsignedShopByTamerIdQuery(client.TamerId));
                if (shopDto == null)
                {
                    await GameLogger.LogWarning($"No shop found for TamerId={client.TamerId}", logFolder);
                    client.Tamer.RestorePreviousCondition();
                    client.Send(new ConsignedShopClosePacket());
                    return;
                }

                var shop = _mapper.Map<ConsignedShop>(shopDto);

                var freshSellerDto = await _sender.Send(new CharacterAndItemsByIdQuery(client.TamerId));
                if (freshSellerDto == null)
                {
                    await GameLogger.LogError($"freshSellerDto was null for TamerId={client.TamerId}", logFolder);
                    client.Tamer.RestorePreviousCondition();
                    client.Send(new ConsignedShopClosePacket());
                    return;
                }

                var freshSeller = _mapper.Map<CharacterModel>(freshSellerDto);
                client.Tamer.ConsignedShopItems.Clear();

                if (freshSeller.ConsignedShopItems?.Items != null)
                {
                    foreach (var item in freshSeller.ConsignedShopItems.Items)
                    {
                        var info = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId);
                        item.SetItemInfo(info);
                    }
                    client.Tamer.ConsignedShopItems.AddItems(freshSeller.ConsignedShopItems.Items.Clone());
                }
                else
                {
                    await GameLogger.LogWarning("freshSeller.ConsignedShopItems.Items was null", logFolder);
                }

                var items = client.Tamer.ConsignedShopItems.Items.Clone();
                items.ForEach(i =>
                    i.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == i.ItemId)));

                client.Tamer.ConsignedShopItems.RemoveOrReduceItems(items.Clone());
                await _sender.Send(new DeleteConsignedShopCommand(shop.GeneralHandler));

                client.Tamer.ConsignedWarehouse.AddItems(items.Clone());
                await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedWarehouse));

                _mapServer.BroadcastForTamerViewsAndSelf(
                    client.TamerId,
                    new UnloadConsignedShopPacket(shop).Serialize());

                await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedShopItems));

                client.Tamer.RestorePreviousCondition();
                client.Send(new ConsignedShopClosePacket());

                await GameLogger.LogInfo("[SUCCESS] Shop retrieved and items moved successfully", logFolder);
            }
            catch (Exception ex)
            {
                await GameLogger.LogError($"[ERROR] {ex.Message}", logFolder);
                client.Tamer.RestorePreviousCondition();
                client.Send(new ConsignedShopClosePacket());
            }
        }
    }
}
