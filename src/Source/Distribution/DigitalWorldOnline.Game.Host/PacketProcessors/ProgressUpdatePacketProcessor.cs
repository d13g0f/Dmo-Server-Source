using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ProgressUpdatePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ProgressUpdate;

        private readonly ISender _sender;
        private readonly ILogger _logger;

        public ProgressUpdatePacketProcessor(
            ISender sender,
            ILogger logger
        )
        {
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var objectId = packet.ReadInt();
            if (client.blockAchievement)
            {
                client.Send(new SystemMessagePacket("$ For I am HERE — All Might is here!!"));
                client.SetGameQuit(true);
                client.Disconnect();
                return;
            }

            UpdateProgressValue(client, objectId);

            await _sender.Send(new UpdateCharacterProgressCompleteCommand(client.Tamer.Progress));
        }

        private void UpdateProgressValue(GameClient client, int questId)
        {
            UpdateQuestComplete(client, questId);
        }

        private void UpdateQuestComplete(GameClient client, int qIDX)
        {
            int index = qIDX - 1;
            int arrIDX = index / 32;

            // Step 1: Copy the array to a local variable
            var completedData = client.Tamer.Progress.CompletedDataValue;

            // Step 2: Ensure the array is large enough
            EnsureArrayCapacity(ref completedData, arrIDX);

            // Step 3: Write back to the Progress property
            client.Tamer.Progress.CompletedDataValue = completedData;

            // Step 4: Modify the array
            int intValue = GetBitValue(completedData, index);

            if (intValue == 0)
                SetBitValue(completedData, index, 1);
        }

        private void EnsureArrayCapacity(ref int[] array, int requiredIndex)
        {
            if (array == null || array.Length <= requiredIndex)
            {
                int newLength = requiredIndex + 1;
                _logger.Warning($"Resizing CompletedDataValue array to {newLength} to support quest index.");
                Array.Resize(ref array, newLength);
            }
        }

        private int GetBitValue(int[] array, int x)
        {
            int arrIDX = x / 32;
            int bitPosition = x % 32;

            int value = array[arrIDX];
            return (value >> bitPosition) & 1;
        }

        private void SetBitValue(int[] array, int x, int bitValue)
        {
            int arrIDX = x / 32;
            int bitPosition = x % 32;

            if (bitValue != 0 && bitValue != 1)
            {
                _logger.Error($"Invalid bit value. Only 0 or 1 are allowed for achievement {x}.");
                throw new ArgumentException("Invalid bit value. Only 0 or 1 are allowed.");
            }

            int value = array[arrIDX];
            int mask = 1 << bitPosition;

            if (bitValue == 1)
                array[arrIDX] = value | mask;
            else
                array[arrIDX] = value & ~mask;
        }
    }
}
