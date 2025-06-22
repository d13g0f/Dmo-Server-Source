using System;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Packets.GameServer;
using MediatR;
using Microsoft.Data.SqlClient;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    internal class BanForCheating
    {
        public ILogger _logger;
        public ISender _sender;

        public BanForCheating(ILogger logger, ISender sender)
        {
            _logger = logger;
            _sender = sender;
        }

        public async void BanAccountForCheating(long accountId, AccountBlockEnum type, string reason, DateTime startDate, DateTime endDate)
        {
            try
            {
                await _sender.Send(new AddAccountBlockCommand(accountId, type, reason, startDate, endDate));
                _logger.Verbose($"Account ID: {accountId} has been banned");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to ban account id {accountId} and error: {ex.Message}");
            }
        }

        public string BanAccountWithMessage(long accountId, string Name, AccountBlockEnum type, string reason, GameClient? client = null, string? banMessage = null)
        {
            var startDate = DateTime.Now;
            var endDate = DateTime.Now;

            if (type == AccountBlockEnum.Short)
            {
                endDate = DateTime.UtcNow.AddDays(1);
            }
            else if (type == AccountBlockEnum.Medium)
            {
                endDate = DateTime.UtcNow.AddDays(3);
            }
            else if (type == AccountBlockEnum.Long)
            {
                endDate = DateTime.UtcNow.AddDays(5);
            }
            else
            {
                endDate = DateTime.MaxValue;
            }

            BanAccountForCheating(accountId, type, reason, startDate, endDate);
            
            if (client != null)
            {
                TimeSpan timeRemaining = endDate - startDate;

                uint secondsRemaining = (uint)timeRemaining.TotalSeconds;

                client.Send(new BanUserPacket(secondsRemaining, banMessage ?? reason));
            }

            return $"User {Name} has been banned for: {reason}.";
        }
    }
}