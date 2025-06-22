using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class QuestGiveUpPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.QuestGiveUp;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public QuestGiveUpPacketProcessor(AssetsLoader assets, ILogger logger, ISender sender)
        {
            _assets = assets;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var questId = packet.ReadShort();

            try
            {
                var questInfo = _assets.Quest.FirstOrDefault(x => x.QuestId == questId);

                if (questInfo == null)
                {
                    _logger.Error($"[QuestGiveUp] :: Tamer {client.Tamer.Name} tryed to giveUp Quest with Id {questId} not in database !!");
                    client.Disconnect();
                    return;
                }

                _logger.Information($"Character {client.TamerId} gave up quest {questId}.");

                var id = client.Tamer.Progress.RemoveQuest(questId);

                await _sender.Send(new RemoveActiveQuestCommand(id));
            }
            catch (Exception ex)
            {
                _logger.Error($"[QuestGiveUp] :: {ex.Message}");
            }
        }
    }
}
