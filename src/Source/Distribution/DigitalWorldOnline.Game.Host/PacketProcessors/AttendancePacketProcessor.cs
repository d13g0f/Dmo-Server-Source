using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class AttendancePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.Attendance;

        private readonly ISender _sender;
        private readonly ILogger _logger;

        public AttendancePacketProcessor(ISender sender, ILogger logger)
        {
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            _logger.Information($"Reading Attendance Packet !!");

            int nResCode = 0;
            var attendenceReward = client.Tamer.AttendanceReward;
            uint nGiveItemNo = (uint)attendenceReward.LastRewardDate.Day;
            int nWorkDayHistory = attendenceReward.LastRewardDate.Day;

            if (attendenceReward.LastRewardDate.Day == attendenceReward.TotalDays)
            {
                nResCode = 1;
            }

            _logger.Information($"nResCode: {nResCode} | nGiveItemNo: {nGiveItemNo} | nWorkDayHistory: {nWorkDayHistory} |");

            //await _sender.Send(new RecvAttendancePacket(nResCode, nGiveItemNo, nWorkDayHistory));
        }
    }
}