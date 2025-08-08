using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using GameServer.Logging;
using MediatR;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedWarehouseRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedWarehouseRetrieve;

        private readonly ISender _sender;

        public ConsignedWarehouseRetrievePacketProcessor(ISender sender)
        {
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var tamer = client.Tamer;
            var inventory = tamer.Inventory;
            var warehouse = tamer.ConsignedWarehouse;

            var itemsToRetrieve = warehouse.Items.Clone();
            var bitsToRetrieve = warehouse.Bits;

            // Registro inicial
            await GameLogger.LogInfo(
                $"[ConsignedRetrieve] Start retrieve: Player={tamer.Name}, CharacterId={tamer.Id}, Items={itemsToRetrieve.Count}, Bits={bitsToRetrieve}",
                "shops"
            );

            // Actualizamos warehouse
            warehouse.RemoveOrReduceItems(itemsToRetrieve.Clone());
            warehouse.RemoveBits(bitsToRetrieve);

            // Añadimos a inventario
            inventory.AddItems(itemsToRetrieve.Clone());
            inventory.AddBits(bitsToRetrieve);

            // Enviamos los paquetes actualizados al cliente
            client.Send(new LoadInventoryPacket(inventory, InventoryTypeEnum.Inventory));
            client.Send(new LoadConsignedShopWarehousePacket(warehouse));
            client.Send(new ConsignedShopWarehouseItemRetrievePacket());

            // Guardado en base de datos
            await _sender.Send(new UpdateItemsCommand(inventory));
            await _sender.Send(new UpdateItemListBitsCommand(inventory));
            await _sender.Send(new UpdateItemsCommand(warehouse));
            await _sender.Send(new UpdateItemListBitsCommand(warehouse));

            // Confirmación final
            await GameLogger.LogInfo(
                $"[ConsignedRetrieve] Finished retrieve: Player={tamer.Name}, CharacterId={tamer.Id}, RetrievedItems={itemsToRetrieve.Count}, RetrievedBits={bitsToRetrieve}",
                "shops"
            );
        }
    }
}
