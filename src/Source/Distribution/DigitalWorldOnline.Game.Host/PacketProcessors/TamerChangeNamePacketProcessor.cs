using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class TamerChangeNamePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TamerChangeName;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public TamerChangeNamePacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ILogger logger,
            ISender sender)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            int itemSlot = packet.ReadInt();
            string newName = packet.ReadString();
            string oldName = client.Tamer.Name;

            bool isAvailableName = await _sender.Send(new CharacterByNameQuery(newName)) == null;

            if (!isAvailableName)
            {
                client.Send(new TamerChangeNamePacket(CharacterChangeNameType.Existing, oldName, newName, itemSlot));
                return;
            }

            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);

            if (inventoryItem != null)
            {
                if (inventoryItem.ItemInfo.Section != 15200)
                {
                    // Uncomment if you want to enforce permanent ban for cheating
                    // var banProcessor = SingletonResolver.GetService<BanForCheating>();
                    // var banMessage = banProcessor.BanAccountWithMessage(client.AccountId, client.Tamer.Name, AccountBlockEnum.Permanent, "Cheating", client, "Attempted to cheat name change.");

                    // client.SendToAll(new NoticeMessagePacket(banMessage).Serialize());

                    client.Disconnect();
                    _logger.Warning($"[DISCONNECTED] :: {client.Tamer.Name} attempted to cheat name change.");
                    return;
                }

                client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);
                client.Tamer.UpdateName(newName);

                await _sender.Send(new ChangeTamerNameByIdCommand(client.Tamer.Id, newName));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Send(new TamerChangeNamePacket(CharacterChangeNameType.Sucess, itemSlot, oldName, newName));
                client.Send(new TamerChangeNamePacket(CharacterChangeNameType.Complete, newName, newName, itemSlot));

                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                var packetGroup = UtilitiesFunctions.GroupPackets(
                    new UnloadTamerPacket(client.Tamer).Serialize(),
                    new LoadTamerPacket(client.Tamer).Serialize()
                );

                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTamerViews(client.TamerId, packetGroup);
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViews(client, packetGroup);
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViews(client, packetGroup);
                        break;
                    default:
                        _mapServer.BroadcastForTamerViews(client, packetGroup);
                        break;
                }

                var friendsIds = client.Tamer.Friended.Select(x => x.CharacterId).ToList();
                var foesIds = client.Tamer.Foed.Select(x => x.CharacterId).ToList();

                var friendPacket = new FriendChangeNamePacket(oldName, newName, false).Serialize();
                var foePacket = new FriendChangeNamePacket(oldName, newName, true).Serialize();

                // Broadcast to friends
                _mapServer.BroadcastForTargetTamers(friendsIds, friendPacket);
                _dungeonServer.BroadcastForTargetTamers(friendsIds, friendPacket);
                _eventServer.BroadcastForTargetTamers(friendsIds, friendPacket);
                _pvpServer.BroadcastForTargetTamers(friendsIds, friendPacket);

                // Broadcast to foes
                _mapServer.BroadcastForTargetTamers(foesIds, foePacket);
                _dungeonServer.BroadcastForTargetTamers(foesIds, foePacket);
                _eventServer.BroadcastForTargetTamers(foesIds, foePacket);
                _pvpServer.BroadcastForTargetTamers(foesIds, foePacket);

                _logger.Verbose($"Character {client.TamerId} changed name from '{oldName}' to '{newName}'.");
            }
        }
    }
}
