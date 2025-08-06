using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.Items;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchRemoveEggPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchRemoveEgg;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public HatchRemoveEggPacketProcessor(
            AssetsLoader assets,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        // Processes the packet to remove an egg from the incubator.
        // Includes explicit validations to ensure safe removal and prevent duplication.
      
        public async Task Process(GameClient client, byte[] packetData)
        {
            _ = GameLogger.LogInfo("Hatch", $"[RemoveEgg] Iniciado por TamerId: {client.TamerId}. EggId={client.Tamer.Incubator.EggId}, IsBusy={client.Tamer.Incubator.IsBusy}");

            // Check if there is an undeveloped egg to remove
            if (!client.Tamer.Incubator.NotDevelopedEgg)
            {
                _ = GameLogger.LogInfo("Hatch", $"[RemoveEgg] No hay huevo sin desarrollar para TamerId: {client.TamerId}. Nada que remover.");
                client.Tamer.Incubator.RemoveEgg();
                await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
                return;
            }

            // Validate egg ID
            if (client.Tamer.Incubator.EggId <= 0)
            {
                _ = GameLogger.LogWarning("Hatch", $"[RemoveEgg] EggId inválido: {client.Tamer.Incubator.EggId} para TamerId: {client.TamerId}.");
                client.Send(new SystemMessagePacket("No valid egg to remove."));
                return;
            }

            // Validate item info existence
            var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == client.Tamer.Incubator.EggId);
            if (itemInfo == null)
            {
                _ = GameLogger.LogWarning("Hatch", $"[RemoveEgg] ItemInfo no encontrado para EggId: {client.Tamer.Incubator.EggId} para TamerId: {client.TamerId}.");
                client.Send(new SystemMessagePacket("Item not found. Cannot return to inventory."));
                return;
            }

            // Create and clone item to return to inventory
            var newItem = new ItemModel();
            newItem.SetItemInfo(itemInfo);
            newItem.SetItemId(client.Tamer.Incubator.EggId);
            newItem.SetAmount(1);
            var cloneItem = (ItemModel)newItem.Clone();

            // Attempt to add item to inventory
            if (!client.Tamer.Inventory.AddItem(cloneItem))
            {
                _ = GameLogger.LogWarning("Hatch", $"[RemoveEgg] Inventario lleno: No se pudo devolver huevo {client.Tamer.Incubator.EggId} para TamerId: {client.TamerId}.");
                client.Send(new SystemMessagePacket("Inventory is full. Cannot return egg."));
                return;
            }

            // Log successful inventory addition and clear incubator
            _ = GameLogger.LogInfo("Hatch", $"[RemoveEgg] TamerId: {client.TamerId} removió huevo {client.Tamer.Incubator.EggId} de la incubadora y lo añadió al inventario.");
            client.Tamer.Incubator.RemoveEgg();

            // Update game state
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }
    }
}