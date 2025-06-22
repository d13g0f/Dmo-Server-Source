using DigitalWorldOnline.Commons.Models.Chat;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Create
{
    public class CreateCommandMessageCommand : IRequest
    {
        public CommandMessageModel CommandMessage { get; private set; }

        public CreateCommandMessageCommand(CommandMessageModel commandMessage)
        {
            CommandMessage = commandMessage;
        }
    }
}