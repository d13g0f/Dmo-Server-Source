using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Create
{
    public class CreateCommandMessageCommandHandler : IRequestHandler<CreateCommandMessageCommand>
    {
        private readonly ICharacterCommandsRepository _repository;

        public CreateCommandMessageCommandHandler(ICharacterCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(CreateCommandMessageCommand request, CancellationToken cancellationToken)
        {
            await _repository.AddCommandMessageAsync(request.CommandMessage);

            return Unit.Value;
        }
    }
}