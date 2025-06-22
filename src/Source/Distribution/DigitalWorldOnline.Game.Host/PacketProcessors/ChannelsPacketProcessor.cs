using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ChannelsPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.Channels;

        private readonly ILogger _logger;

        public ChannelsPacketProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            _logger.Debug($"Sending only channel 0...");

            var channels = new Dictionary<byte, byte>
            {
                { 0, 0 } // 0:smooth
            };

            client.Send(new AvailableChannelsPacket(channels).Serialize());

            await Task.CompletedTask;
        }
    }
}
