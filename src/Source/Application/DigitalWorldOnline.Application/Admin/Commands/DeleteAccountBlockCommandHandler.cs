using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class DeleteAccountBlockCommandHandler : IRequestHandler<DeleteAccountBlockCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public DeleteAccountBlockCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(DeleteAccountBlockCommand request, CancellationToken cancellationToken)
        {
            await _repository.DeleteAccountBlockAsync(request.Id);

            return Unit.Value;
        }
    }
}