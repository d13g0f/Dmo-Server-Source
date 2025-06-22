using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Routines.Commands
{
    public class ExecuteDailyRewardRoutineCommandHandler : IRequestHandler<ExecuteDailyRewardRoutineCommand>
    {
        private readonly IRoutineRepository _repository;

        public ExecuteDailyRewardRoutineCommandHandler(IRoutineRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(ExecuteDailyRewardRoutineCommand request, CancellationToken cancellationToken)
        {
            await _repository.ExecuteDailyRewardsAsync();

            return Unit.Value;
        }
    }
}