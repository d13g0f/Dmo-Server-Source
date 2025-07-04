using System;
using System.Linq;
using System.Threading.Tasks;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Game;
using DigitalWorldOnline.Game.PacketProcessors;
using GameServer.Logging;
using MediatR;


namespace DigitalWorldOnline.Game.Security

{
    public static class AntiCheatSystem
    {
        private static AssetsLoader? _assets;
        public static void Initialize(AssetsLoader assets)
        {
            _assets = assets;
        }

        /// <summary>
        /// Verifica la integridad de los objetos en el inventario del jugador.
        /// </summary>
        public static async Task<bool> ValidateInventoryIntegrity(GameClient client)
        {
        var characterRepo = SingletonResolver.GetService<ICharacterCommandsRepository>();
            if (_assets == null)
                throw new InvalidOperationException("AntiCheatSystem not initialized with assets.");

            for (int itemSlot = 0; itemSlot < client.Tamer.Inventory.Size; itemSlot++)
            {
                var targetItem = client.Tamer.Inventory.FindItemBySlotCheck(itemSlot);
                if (targetItem == null || targetItem.ItemId == 0)
                    continue;

                var targetItemTrue = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetItem.ItemId);
                if (targetItemTrue == null)
                {
                    await GameLogger.LogWarning($"Invalid item detected: {targetItem.ItemId}", "anti-cheat/items");
                    client.Send(new SystemMessagePacket("Invalid item data."));
                    return false;
                }

                if (targetItem.Amount > 999)
                {
                    await GameLogger.LogWarning($"Item over limit: {targetItem.ItemId} x{targetItem.Amount}", "anti-cheat/items");
                    var banProcessor = SingletonResolver.GetService<BanForCheating>();
                    var banMessage = banProcessor.BanAccountWithMessage(client.AccountId, client.Tamer.Name,
                        AccountBlockEnum.Permanent, "Item duplication detected", client,
                        "Create a ticket if you're Innocent.");

                    client.SendToAll(new NoticeMessagePacket(banMessage).Serialize());
                    client.Disconnect();
                    return false;
                }
            }

            return true;
        }
            
    public static async Task<bool> ValidatePurchaseBits(GameClient client, long expectedCost)
    {
        if (expectedCost <= 0)
        {
            await GameLogger.LogWarning($"[AntiCheat] Invalid cost: {expectedCost}", "anti-cheat/bits");
            return false;
        }

        var characterRepo = SingletonResolver.GetService<ICharacterCommandsRepository>();
        var dbBits = await characterRepo.GetInventoryBitsByItemListIdAsync(client.Tamer.Inventory.Id);

        if (!dbBits.HasValue)
        {
            await GameLogger.LogWarning($"[AntiCheat] Could not retrieve bits for Tamer {client.Tamer.Name}", "anti-cheat/bits");
            return false;
        }

        if (dbBits.Value < expectedCost)
        {
            await GameLogger.LogWarning($"[AntiCheat] Tamer {client.Tamer.Name} tried to spend {expectedCost} bits but DB has {dbBits.Value}", "anti-cheat/bits");

            var banProcessor = SingletonResolver.GetService<BanForCheating>();
            var banMessage = banProcessor.BanAccountWithMessage(
                client.AccountId,
                client.Tamer.Name,
                AccountBlockEnum.Permanent,
                "Bit spoofing in Personal Shop",
                client,
                "Create a ticket if you're innocent."
            );

            client.SendToAll(new NoticeMessagePacket(banMessage).Serialize());
            client.Disconnect();
            return false;
        }

        return true;
    }







}

}


