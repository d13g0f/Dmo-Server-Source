using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class DeleteAccountBlockCommand : IRequest
    {
        public long Id { get; set; }

        public DeleteAccountBlockCommand(long id)
        {
            Id = id;
        }
    }
}