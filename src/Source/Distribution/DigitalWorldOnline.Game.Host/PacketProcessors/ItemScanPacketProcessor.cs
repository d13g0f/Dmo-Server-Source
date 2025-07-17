using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.DTOs.Base;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Infrastructure.Migrations;
using MediatR;
using Serilog;
using System.Net.Mime;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;


namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemScanPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ItemScan;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public ItemScanPacketProcessor(
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonsServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
        }

     public async Task Process(GameClient client, byte[] packetData)
    {
        var packet = new GamePacketReader(packetData);

        var vipEnabled = packet.ReadByte();
        var u2 = packet.ReadInt();
        var npcId = packet.ReadInt();
        var slotToScan = packet.ReadInt();
        var amountToScan = packet.ReadShort();

        await client.MoveItemLock.WaitAsync();
        try
        {
            var scannedItem = client.Tamer.Inventory.FindItemBySlot(slotToScan);
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            if (scannedItem == null || scannedItem.ItemId == 0 || scannedItem.ItemInfo == null)
            {
                await GameLogger.LogError($"[ItemScan] {client.Tamer.Name} inconsistency: invalid scanned item in slot {slotToScan}.", "item_scan");
                return;
            }

            if (client.Tamer.Inventory.CountItensById(scannedItem.ItemId) < amountToScan)
            {
                await GameLogger.LogError($"[ItemScan] {client.Tamer.Name} inconsistency: not enough items to scan. Requested {amountToScan} but has less.", "item_scan");
                return;
            }

            var scanAsset = _assets.ScanDetail.FirstOrDefault(x => x.ItemId == scannedItem.ItemId);
            if (scanAsset == null)
            {
                await GameLogger.LogWarning($"[ItemScan] {client.Tamer.Name} tried to scan item {scannedItem.ItemId} but no scan configuration found.", "item_scan");

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new SystemMessagePacket($"No scan configuration for item id {scannedItem.ItemId}.").Serialize(),
                        new ItemScanFailPacket(client.Tamer.Inventory.Bits, slotToScan, scannedItem.ItemId).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );

                return;
            }

            var receivedRewards = new Dictionary<int, ItemModel>();
            short scannedItens = 0;
            long cost = 0;
            var error = false;

            while (scannedItens < amountToScan && !error)
            {
                if (!scanAsset.Rewards.Any())
                {
                    await GameLogger.LogWarning($"[ItemScan] Scan config for item {scanAsset.ItemId} has no rewards.", "item_scan");
                    client.Send(new SystemMessagePacket($"Scan config for item {scanAsset.ItemId} has incorrect rewards configuration."));
                    break;
                }

                var possibleRewards = scanAsset.Rewards.OrderBy(x => Guid.NewGuid()).ToList();
                foreach (var possibleReward in possibleRewards)
                {
                    if (cost + scannedItem.ItemInfo.ScanPrice > client.Tamer.Inventory.Bits)
                    {
                        await GameLogger.LogWarning($"[ItemScan] {client.Tamer.Name} out of bits during scan.", "item_scan");
                        error = true;
                        break;
                    }

                    if (possibleReward.Chance >= UtilitiesFunctions.RandomDouble())
                    {
                        var itemRewardAmount = UtilitiesFunctions.RandomInt(possibleReward.MinAmount, possibleReward.MaxAmount);

                        var contentItem = new ItemModel();
                        contentItem.SetItemId(possibleReward.ItemId);
                        contentItem.SetAmount(itemRewardAmount);
                        contentItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == possibleReward.ItemId));

                        if (contentItem.ItemInfo == null)
                        {
                            await GameLogger.LogError($"[ItemScan] {client.Tamer.Name} received reward with invalid ItemInfo for {possibleReward.ItemId}.", "item_scan");
                            client.Send(new SystemMessagePacket($"Invalid item info for item {possibleReward.ItemId}."));
                            error = true;
                            break;
                        }

                        if (contentItem.ItemInfo.Section == 5200)
                        {
                            var ChipsetItem = ApplyValuesChipset(contentItem);
                        }

                        if (contentItem.IsTemporary)
                            contentItem.SetRemainingTime((uint)contentItem.ItemInfo.UsageTimeMinutes);

                        var targetSlot = client.Tamer.Inventory.FindAvailableSlot(contentItem);

                        if (targetSlot != client.Tamer.Inventory.GetEmptySlot)
                        {
                            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(targetSlot);
                            var tempItem = (ItemModel)inventoryItem.Clone();
                            tempItem.IncreaseAmount(contentItem.Amount);

                            if (!receivedRewards.ContainsKey(targetSlot))
                                receivedRewards.Add(targetSlot, tempItem);
                            else
                                receivedRewards[targetSlot].IncreaseAmount(contentItem.Amount);
                        }
                        else
                        {
                            var tempItem = (ItemModel)contentItem.Clone();

                            if (!receivedRewards.ContainsKey(targetSlot))
                                receivedRewards.Add(targetSlot, tempItem);
                            else
                                receivedRewards[targetSlot].IncreaseAmount(contentItem.Amount);
                        }

                        if (client.Tamer.Inventory.AddItem(contentItem))
                        {
                            if (possibleReward.Rare)
                            {
                                client.SendToAll(new NeonMessagePacket(NeonMessageTypeEnum.Item, client.Tamer.Name,
                                    scanAsset.ItemId, possibleReward.ItemId).Serialize());
                            }

                            cost += scannedItem.ItemInfo.ScanPrice;
                            scannedItens++;
                        }
                        else
                        {
                            await GameLogger.LogWarning($"[ItemScan] {client.Tamer.Name} no more space in inventory while scanning.", "item_scan");
                            error = true;
                            break;
                        }
                    }

                    if (scannedItens >= amountToScan || error)
                        break;
                }
            }

            client.Send(new ItemScanSuccessPacket(
                cost,
                client.Tamer.Inventory.Bits - cost,
                slotToScan,
                scannedItem.ItemId,
                scannedItens,
                receivedRewards));

            var dropList = string.Join(',', receivedRewards.Select(x => $"{x.Value.ItemId} x{x.Value.Amount}"));

            await GameLogger.LogInfo($"[ItemScan] {client.Tamer.Name} scanned {scannedItem.ItemId} x{scannedItens} cost {cost} drops: {dropList}", "item_scan");

            client.Tamer.Inventory.RemoveBits(cost);
            client.Tamer.Inventory.RemoveOrReduceItem(scannedItem, scannedItens, slotToScan);

            var scanQuest = client.Tamer.Progress.InProgressQuestData.FirstOrDefault(x => x.QuestId == 4021);
            if (scanQuest != null && scanAsset.ItemId == 9071)
            {
                scanQuest.UpdateCondition(0, 1);
                client.Send(new QuestGoalUpdatePacket(4021, 0, 1));
                var questToUpdate = client.Tamer.Progress.InProgressQuestData.FirstOrDefault(x => x.QuestId == 4021);
                await _sender.Send(new UpdateCharacterInProgressCommand(questToUpdate));
            }

            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
        }
        finally
        {
            client.MoveItemLock.Release();
        }
    }

        private ItemModel ApplyValuesChipset(ItemModel newItem)
        {
            // Local RNG optimizado
            var rng = new Random();

            var chipsetInfo = _assets.SkillCodeInfo
                .FirstOrDefault(x => x.SkillCode == newItem.ItemInfo.SkillCode)
                ?.Apply.FirstOrDefault(x => x.Type > 0);

            if (chipsetInfo == null)
            {
                _logger.Warning($"[Chipset] No ChipsetInfo found for SkillCode={newItem.ItemInfo.SkillCode}.");
                return newItem;
            }

            var chipsetSkill = _assets.SkillInfo
                .FirstOrDefault(x => x.SkillId == newItem.ItemInfo.SkillCode)
                ?.FamilyType ?? 0;

            // RNG entre rangos definidos
            int applyRate = rng.Next(newItem.ItemInfo.ApplyValueMin, newItem.ItemInfo.ApplyValueMax + 1);

            var nValue = chipsetInfo.Value + (newItem.ItemInfo.TypeN * chipsetInfo.AdditionalValue);
            int finalValue = (int)((double)applyRate * nValue / 100);

            // Ordena slots para consistencia
            newItem.AccessoryStatus = newItem.AccessoryStatus.OrderBy(x => x.Slot).ToList();

            var possibleStatus = (AccessoryStatusTypeEnum)chipsetInfo.Attribute;

            newItem.AccessoryStatus[0].SetType(possibleStatus);
            newItem.AccessoryStatus[0].SetValue((short)finalValue);

            newItem.SetPower((byte)applyRate);
            newItem.SetReroll(100);
            newItem.SetFamilyType(chipsetSkill);

            _logger.Verbose($"[Chipset] Applied: SkillCode={newItem.ItemInfo.SkillCode} Rate={applyRate} Final={finalValue} Status={possibleStatus} FamilyType={chipsetSkill}");

            return newItem;
        }

    }
}