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
            try
            {
                _logger.Debug(">> ConsignedShopRetrieve START for TamerId={TamerId}", client.TamerId);

                // 1) Obtener el shop original
                _logger.Debug("Calling ConsignedShopByTamerIdQuery...");
                var shopDto = await _sender.Send(new ConsignedShopByTamerIdQuery(client.TamerId));
                if (shopDto == null)
                {
                    _logger.Warning("No consigned shop found for TamerId={TamerId}", client.TamerId);
                    client.Tamer.RestorePreviousCondition();
                    client.Send(new ConsignedShopClosePacket());
                    return;
                }

                var shop = _mapper.Map<ConsignedShop>(shopDto);
                _logger.Debug("Found shop with handler={Handler}", shop.GeneralHandler);

                // 2) Refrescar modelo completo del vendedor
                _logger.Debug("Calling CharacterAndItemsByIdQuery...");
                var freshSellerDto = await _sender.Send(new CharacterAndItemsByIdQuery(client.TamerId));
                if (freshSellerDto == null)
                {
                    _logger.Error("CharacterAndItemsByIdQuery returned null for TamerId={TamerId}", client.TamerId);
                    client.Tamer.RestorePreviousCondition();
                    client.Send(new ConsignedShopClosePacket());
                    return;
                }

                var freshSeller = _mapper.Map<CharacterModel>(freshSellerDto);

                _logger.Debug("Replacing in-memory ConsignedShopItems...");
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
                    _logger.Warning("freshSeller.ConsignedShopItems.Items was null for TamerId={TamerId}", client.TamerId);
                }

                // 3) Clonar items para devolver
                var items = client.Tamer.ConsignedShopItems.Items.Clone();
                items.ForEach(i =>
                    i.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == i.ItemId))
                );

                // 4) Retirar en memoria y eliminar de BD
                _logger.Debug("Removing consigned shop items in memory...");
                client.Tamer.ConsignedShopItems.RemoveOrReduceItems(items.Clone());

                _logger.Debug("Sending DeleteConsignedShopCommand({Handler})", shop.GeneralHandler);
                await _sender.Send(new DeleteConsignedShopCommand(shop.GeneralHandler));

                // 5) Devolver a warehouse y actualizar BD
                _logger.Debug("Adding items to warehouse & updating DB...");
                client.Tamer.ConsignedWarehouse.AddItems(items.Clone());
                await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedWarehouse));

                // 6) Notificar a otros clientes
                _mapServer.BroadcastForTamerViewsAndSelf(
                    client.TamerId,
                    new UnloadConsignedShopPacket(shop).Serialize());

                // 7) Actualizar también la vista de shop y cerrar
                _logger.Debug("Updating shop items in DB...");
                await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedShopItems));

                _logger.Debug(">> ConsignedShopRetrieve SUCCESS, sending close packet");

                client.Tamer.RestorePreviousCondition();
                client.Send(new ConsignedShopClosePacket());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ConsignedShopRetrievePacketProcessor.Process for TamerId={TamerId}", client.TamerId);
                client.Tamer.RestorePreviousCondition();
                client.Send(new ConsignedShopClosePacket());
            }
        }


    }
}
