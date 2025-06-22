using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateAccountDailyRewardCommandHandler : IRequestHandler<UpdateAccountDailyRewardCommand>
    {
        private readonly IAccountCommandsRepository _repository;

        public UpdateAccountDailyRewardCommandHandler(IAccountCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(UpdateAccountDailyRewardCommand request, CancellationToken cancellationToken)
        {
            await _repository.UpdateAccountDailyRewardAsync(request.AccountId, request.DailyRewardClaimed, request.DailyRewardClaimedAmount);

            return Unit.Value;
        }
    }
}