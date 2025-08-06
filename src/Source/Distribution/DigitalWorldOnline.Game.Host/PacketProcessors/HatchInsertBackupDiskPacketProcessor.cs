using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchInsertBackupDiskPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchInsertBackup;

        private readonly ISender _sender;
        private readonly AssetsLoader _assets;

        public HatchInsertBackupDiskPacketProcessor(
            ISender sender,
            AssetsLoader assets)
        {
            _sender = sender;
            _assets = assets;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            byte vipEnabled = packet.ReadByte(); // No usado por ahora
            short itemSlot = packet.ReadShort();

            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);

            if (inventoryItem == null)
            {
               
              _ = GameLogger.LogInfo("HatchInsertBackupDiskPacketProcessor", $"TamerId={client.TamerId} tried to insert a backup disk from invalid slot={itemSlot}");
                return;
            }

            // Validación opcional: podrías tener un enum o lista de IDs válidos de Backup Disk
            if (!IsValidBackupDisk(inventoryItem.ItemId))
            {
                
              _ = GameLogger.LogWarning("HatchInsertBackupDiskPacketProcessor", $"TamerId={client.TamerId} tried to insert invalid backup disk itemId={inventoryItem.ItemId}");
                return;
            }

            client.Tamer.Incubator.InsertBackupDisk(inventoryItem.ItemId);

           
          _ = GameLogger.LogInfo("HatchInsertBackupDiskPacketProcessor", $"TamerId={client.TamerId} inserted BackupDisk ItemId={inventoryItem.ItemId}");

            bool itemRemoved = client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);

            if (!itemRemoved)
            {
              _ = GameLogger.LogInfo("HatchInsertBackupDiskPacketProcessor", $"TamerId={client.TamerId} failed to remove BackupDisk ItemId={inventoryItem.ItemId} from slot={itemSlot}");
                return;
            }

            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }

        private bool IsValidBackupDisk(int itemId)
        {
            var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId);
            if (itemInfo == null)
                return false;

            return itemInfo.Type == 56 || itemInfo.Type == 170 || itemInfo.Type == 171;
        }


    }
}
