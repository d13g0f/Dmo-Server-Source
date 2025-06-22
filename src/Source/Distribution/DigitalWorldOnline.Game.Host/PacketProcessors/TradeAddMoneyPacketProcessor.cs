using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class TradeAddMoneyacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TradeAddMoney;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public TradeAddMoneyacketProcessor(MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer, ILogger logger, ISender sender)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var targetMoney = packet.ReadInt();

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            GameClient? targetClient = mapConfig!.Type switch
            {
                MapTypeEnum.Dungeon => _dungeonServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId),
                MapTypeEnum.Event => _eventServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId),
                MapTypeEnum.Pvp => _pvpServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId),
                _ => _mapServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId),
            };

            // Already added bits before? Prevent changing it
            if (client.Tamer.TradeInventory.Bits > 0)
            {
                CancelTradeForBoth(client, targetClient, "You cannot modify the money after it has been added. Trade canceled.");
                return;
            }

            // Invalid (0 or negative) bits
            if (targetMoney <= 0)
            {
                CancelTradeForBoth(client, targetClient, "Invalid trade amount. Trade canceled.");
                return;
            }

            // Not enough money
            if (client.Tamer.Inventory.Bits < targetMoney)
            {
                CancelTradeForBoth(client, targetClient, "You tried to trade more bits than you have. Trade canceled and logged.");

                var banProcessor = SingletonResolver.GetService<BanForCheating>();
                var banMessage = banProcessor.BanAccountWithMessage(
                    client.AccountId,
                    client.Tamer.Name,
                    AccountBlockEnum.Permanent,
                    "Cheating",
                    client,
                    "You tried to trade invalid amount of bits using a cheat method, so be happy with ban!"
                );

                var chatPacket = new NoticeMessagePacket(banMessage).Serialize();
                client.SendToAll(chatPacket);
                return;
            }

            // Deduct and apply bits
            client.Tamer.TradeInventory.AddBits(targetMoney);

            // Notify both sides
            client.Send(new TradeInventoryUnlockPacket(client.Tamer.TargetTradeGeneralHandle));
            targetClient?.Send(new TradeInventoryUnlockPacket(targetClient.Tamer.TargetTradeGeneralHandle));

            client.Send(new TradeAddMoneyPacket(client.Tamer.GeneralHandler, targetMoney));
            targetClient?.Send(new TradeAddMoneyPacket(client.Tamer.GeneralHandler, targetMoney));
        }

        private void CancelTradeForBoth(GameClient client, GameClient? targetClient, string reason)
        {
            // Cancel initiator
            client.Tamer.ClearTrade();
            client.Send(new TradeInventoryUnlockPacket(client.Tamer.TargetTradeGeneralHandle));
            client.Send(new TradeCancelPacket(client.Tamer.GeneralHandler));
            client.Send(new NoticeMessagePacket(reason));

            // Cancel target side if exists
            if (targetClient?.Tamer != null)
            {
                targetClient.Tamer.ClearTrade();
                targetClient.Send(new TradeInventoryUnlockPacket(targetClient.Tamer.TargetTradeGeneralHandle));
                targetClient.Send(new TradeCancelPacket(targetClient.Tamer.GeneralHandler));
                targetClient.Send(new NoticeMessagePacket("Trade was canceled by the other player."));
            }
        }
    }
}
