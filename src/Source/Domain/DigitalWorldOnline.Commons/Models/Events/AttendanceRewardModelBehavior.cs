namespace DigitalWorldOnline.Commons.Models.Events
{
    public sealed partial class AttendanceRewardModel
    {
      public bool ReedemRewards => LastRewardDate.Date < DateTime.Now.Date;

        // Last Reward Date

        public void SetLastRewardDate()
        {
            LastRewardDate = DateTime.Now;  
        }

        public void SetLastRewardDate2()
        {
            LastRewardDate = DateTime.Now.Date;
        }

        // Reward Claimed

        public void SetRewardClaimedToday(bool setReward)
        {
            RewardClaimedToday = setReward;
        }

        public void IncreaseTotalDays(byte amount = 1)
        {
            TotalDays += amount;
        }

        public void SetTotalDays(int amount)
        {
            TotalDays = (byte)amount;
        }

        public void SetTotalDaysToToday()
        {
            TotalDays = (byte)DateTime.Now.Day;
        }

        public void CheckAndResetTotalDays()
        {
            DateTime today = DateTime.Now;

            // Verifica se hoje é o último dia do mês
            if (today.AddDays(1).Month != today.Month)
            {
                TotalDays = 0;
            }
        }
    }
}