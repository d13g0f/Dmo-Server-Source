using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class AccountWarehouseItemRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.RetrivieAccountWarehouseItem;

        private readonly ILogger _logger;
        private readonly ISender _sender;

        public AccountWarehouseItemRetrievePacketProcessor(ILogger logger, ISender sender)
        {
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var itemSlot = packet.ReadShort();

            var result = 0;
            var targetItem = client.Tamer.AccountCashWarehouse.FindItemBySlot(itemSlot);

            if (targetItem == null)
            {
                _logger.Error($"Invalid or missing item in slot {itemSlot}.");
                return;
            }

            if (targetItem.Amount <= 0)
            {
                _logger.Warning($"Invalid or missing item in slot {itemSlot}. Potential hacking attempt.");
                client.Tamer.AccountCashWarehouse.RemoveItem(targetItem, itemSlot);
                client.Tamer.AccountCashWarehouse.Sort();
                return;
            }

            try
            {
                var newItem = (ItemModel)targetItem.Clone();

                if (client.Tamer.Inventory.TotalEmptySlots > 0)
                {
                    result = 0;

                    client.Tamer.AccountCashWarehouse.RemoveItem(targetItem, itemSlot);
                    client.Tamer.AccountCashWarehouse.Sort();

                    client.Send(new AccountWarehouseItemRetrievePacket(newItem, itemSlot, InventoryTypeEnum.Inventory, result));

                    client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));

                    client.Tamer.Inventory.RemoveItem(newItem, (short)newItem.Slot);
                }
                else
                {
                    result = 20150; // This slot is not available.

                    client.Send(new AccountWarehouseItemRetrievePacket(targetItem, itemSlot, InventoryTypeEnum.Inventory, result));
                    client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));

                    _logger.Warning($"Failed to add item in Inventory!! Tamer {client.Tamer.Name} dont have free slots");
                    return;
                }

                if (client.Tamer.Inventory.AddItem(newItem))
                {
                    client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                }
            }
            catch (Exception ex)
            {
                result = 20150; // This slot is not available.

                client.Send(new AccountWarehouseItemRetrievePacket(targetItem, itemSlot, InventoryTypeEnum.Inventory, result));
                client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));

                _logger.Error($"[RetrivieAccountWarehouseItem] :: {ex.Message}");
                _logger.Error($"{ex.InnerException}");
                return;
            }


        }
    }
}