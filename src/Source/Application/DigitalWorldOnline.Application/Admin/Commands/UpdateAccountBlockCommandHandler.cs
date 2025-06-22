using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateAccountBlockCommandHandler : IRequestHandler<UpdateAccountBlockCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public UpdateAccountBlockCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(UpdateAccountBlockCommand request, CancellationToken cancellationToken)
        {
            await _repository.UpdateAccountBlockAsync(request.AccountBlock);

            return Unit.Value;
        }
    }
}