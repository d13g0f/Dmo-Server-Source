using System.Threading.Tasks;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedWarehousePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedWarehouse;

        public Task Process(GameClient client, byte[] packetData)
        {
            client.Send(new LoadConsignedShopWarehousePacket(client.Tamer.ConsignedWarehouse));

            _ = GameLogger.LogInfo(
                $"Warehouse items sent to player: {client.Tamer.Name} (CharacterId={client.Tamer.Id})",
                "shops"
            );

            return Task.CompletedTask;
        }
    }
}
