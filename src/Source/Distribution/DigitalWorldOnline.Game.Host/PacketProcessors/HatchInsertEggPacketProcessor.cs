using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchInsertEggPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchInsertEgg;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
      

        public HatchInsertEggPacketProcessor(AssetsLoader assets, ISender sender, ILogger logger)
        {
            _assets = assets;
            _sender = sender;
        }

        // Processes the packet to insert an egg into the incubator.
        // Includes explicit validations to prevent invalid insertions or duplications.
     
        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            packet.ReadByte(); // vipEnabled: Ignored as per requirement
            var itemSlot = packet.ReadInt();

            // Validate item exists in the specified inventory slot
            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);
            if (inventoryItem == null)
            {
                _ = GameLogger.LogWarning("Hatch", $"[InsertEgg] TamerId: {client.TamerId} intentó insertar un item desde un slot inválido: {itemSlot}.");
                client.Send(new SystemMessagePacket("Item not found in the specified slot."));
                return;
            }

            // Validate item is a hatchable egg
            if (!IsHatchableEgg(inventoryItem.ItemId))
            {
                _ = GameLogger.LogWarning("Hatch", $"[InsertEgg] TamerId: {client.TamerId} intentó insertar un item no incubable: {inventoryItem.ItemId}.");
                client.Send(new SystemMessagePacket("This item cannot be incubated."));
                return;
            }

            // Check if incubator is busy
            if (client.Tamer.Incubator.IsBusy)
            {
                _ = GameLogger.LogWarning("Hatch", $"[InsertEgg] Bloqueado: Incubadora ocupada para TamerId: {client.TamerId}.");
                client.Send(new SystemMessagePacket("An egg is already being incubated. Cancel it before inserting another."));
                return;
            }

            // Check for conflicting egg state (egg present but not developed)
            if (client.Tamer.Incubator.EggId > 0 && client.Tamer.Incubator.HatchLevel == 0)
            {
                _ = GameLogger.LogWarning("Hatch", $"[InsertEgg] Conflicto de estado: Huevo ya presente (EggId={client.Tamer.Incubator.EggId}) para TamerId: {client.TamerId}.");
                client.Send(new SystemMessagePacket("An egg is already in the incubator. Cancel it before inserting another."));
                return;
            }

            // Handle undeveloped egg in incubator
            if (client.Tamer.Incubator.NotDevelopedEgg)
            {
                var oldEggId = client.Tamer.Incubator.EggId;
                var returnItem = new ItemModel();
                returnItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == oldEggId));
                returnItem.SetItemId(oldEggId);
                returnItem.SetAmount(1);

                if (!client.Tamer.Inventory.AddItem((ItemModel)returnItem.Clone()))
                {
                    _ = GameLogger.LogWarning("Hatch", $"[InsertEgg] Inventario lleno para TamerId: {client.TamerId} al devolver huevo previo: {oldEggId}.");
                    client.Send(new SystemMessagePacket("Inventory is full. Cannot return previous egg."));
                    return;
                }

                _ = GameLogger.LogInfo("Hatch", $"[InsertEgg] TamerId: {client.TamerId} removió huevo previo {oldEggId} y lo devolvió al inventario.");
            }

            // Insert new egg and update inventory
            _ = GameLogger.LogInfo("Hatch", $"[InsertEgg] TamerId: {client.TamerId} insertó huevo {inventoryItem.ItemId} desde slot {itemSlot}.");
            client.Tamer.Incubator.InsertEgg(inventoryItem.ItemId);
            client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);

            // Update game state
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }

        // Validates if the item is a hatchable egg based on assets
        private bool IsHatchableEgg(int itemId)
        {
            return _assets.Hatchs?.Any(h => h.ItemId == itemId) ?? false;
        }
    }
}