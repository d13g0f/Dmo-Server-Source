using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchRemoveBackupDiskPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchRemoveBackup;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public HatchRemoveBackupDiskPacketProcessor(
            AssetsLoader assets,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

       public async Task Process(GameClient client, byte[] packetData)
        {
            var incubator = client.Tamer.Incubator;

            if (incubator.BackupDiskId <= 0)
            {
                _logger.Verbose($"Character {client.TamerId} tried to remove backup, but incubator is empty.");
                return;
            }

            var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == incubator.BackupDiskId);
            if (itemInfo == null)
            {
                _logger.Warning($"BackupDiskId {incubator.BackupDiskId} not found in Assets.");
                return;
            }

            var item = new ItemModel();
            item.SetItemInfo(itemInfo);
            item.SetItemId(incubator.BackupDiskId);
            item.SetAmount(1);

            var cloneItem = (ItemModel)item.Clone();

            if (!client.Tamer.Inventory.AddItem(cloneItem))
            {
                _logger.Warning($"Inventory full for incubator recovery of item {incubator.BackupDiskId}.");
                client.Send(new SystemMessagePacket($"Inventory full for incubator recovery of item {incubator.BackupDiskId}."));
                return;
            }

            _logger.Verbose($"Character {client.TamerId} removed backup {incubator.BackupDiskId} from incubator to inventory.");

            incubator.RemoveBackupDisk();

            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(incubator));
        }

    }
}