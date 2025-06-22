using DigitalWorldOnline.Commons.DTOs.Routine;
using DigitalWorldOnline.Commons.Enums;

namespace DigitalWorldOnline.Commons.Interfaces
{
    public interface IRoutineRepository
    {
        Task ExecuteDailyQuestsAsync(List<short> questIdList);

        Task ExecuteDailyRewardsAsync();

        Task<List<RoutineDTO>> GetActiveRoutinesAsync();

        Task UpdateRoutineExecutionTimeAsync(RoutineTypeEnum routineType);
    }
}
