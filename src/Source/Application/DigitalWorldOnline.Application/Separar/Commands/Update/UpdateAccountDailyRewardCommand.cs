using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateAccountDailyRewardCommand : IRequest
    {
        public long AccountId { get; private set; }
        public bool DailyRewardClaimed { get; private set; }
        public int DailyRewardClaimedAmount { get; private set; }

        public UpdateAccountDailyRewardCommand(long accountId, bool dailyRewardClaimed, int dailyRewardClaimedAmount)
        {
            AccountId = accountId;
            DailyRewardClaimed = dailyRewardClaimed;
            DailyRewardClaimedAmount = dailyRewardClaimedAmount;
        }
    }
}
