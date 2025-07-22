using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Writers;
using GameServer.Logging;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class EncyclopediaGetRewardPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.EncyclopediaGetReward;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public EncyclopediaGetRewardPacketProcessor(
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
            var packet = new GamePacketReader(packetData);
            var digimonId = packet.ReadUInt();

            _ =GameLogger.LogInfo($"[Encyclopedia] Trying to get reward for DigimonId: {digimonId}", "DeckBuffReward");

           
            var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == digimonId);
            if (evoInfo == null)
            {
               _ =GameLogger.LogInfo($"[Encyclopedia] EvolutionInfo not found for DigimonId: {digimonId}", "DeckBuffReward");
                client.Send(new SystemMessagePacket("Failed to receive reward."));
                return;
            }

            
            var encyclopedia = client.Tamer.Encyclopedia.FirstOrDefault(x => x.DigimonEvolutionId == evoInfo.Id);
            if (encyclopedia == null)
            {
               _ =GameLogger.LogInfo($"[Encyclopedia] No encyclopedia record for DigimonId: {digimonId}", "DeckBuffReward");
                client.Send(new SystemMessagePacket("Failed to receive reward."));
                return;
            }

            // check flags
            if (encyclopedia.IsRewardReceived)
            {
               _ =GameLogger.LogInfo($"[Encyclopedia] Reward already received for DigimonId: {digimonId}", "DeckBuffReward");
                client.Send(new SystemMessagePacket("You have already received the reward."));
                return;
            }

            if (!encyclopedia.IsRewardAllowed)
            {
               _ =GameLogger.LogInfo($"[Encyclopedia] Reward not allowed yet for DigimonId: {digimonId}", "DeckBuffReward");
                client.Send(new SystemMessagePacket("Failed to receive reward."));
                return;
            }

            
            var itemId = 6546; // TODO: make parameter?
            var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId);
            if (itemInfo == null)
            {
                _ =GameLogger.LogError($"[Encyclopedia] ItemInfo not found for ItemId: {itemId}", "DeckBuffReward");
                client.Send(new SystemMessagePacket("Failed to receive reward."));
                return;
            }

            var newItem = new ItemModel
            {
                ItemId = itemId,
                Amount = 1
            };
            newItem.SetItemInfo(itemInfo);

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)itemInfo.UsageTimeMinutes);

            
            if (client.Tamer.Inventory.AddItem(newItem))
            {
                encyclopedia.SetRewardAllowed(false);
                encyclopedia.SetRewardReceived(true);

               _ =GameLogger.LogInfo($"[Encyclopedia] Reward granted for DigimonId: {digimonId}, ItemId: {itemId}", "DeckBuffReward");

                client.Send(new EncyclopediaReceiveRewardItemPacket(newItem, (int)digimonId));

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
            }
            else
            {
                _ =GameLogger.LogInfo($"[Encyclopedia] Inventory full. Reward could not be granted for DigimonId: {digimonId}", "DeckBuffReward");
                client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
            }
        }
    }
}
