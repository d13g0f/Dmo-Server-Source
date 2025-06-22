using DigitalWorldOnline.Commons.DTOs.Routine;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DigitalWorldOnline.Infrastructure.Repositories.Routine
{
    public class RoutineRepository : IRoutineRepository
    {
        private readonly DatabaseContext _context;

        public RoutineRepository(DatabaseContext context)
        {
            _context = context;
        }

        public class BitwiseOperations
        {
            public static int GetBitValue(int[] array, int x)
            {
                int arrIDX = x / 32;
                int bitPosition = x % 32;

                if (arrIDX >= array.Length)
                {
                    Console.WriteLine($"[ERROR] GetBitValue: Invalid array index. x={x}, arrIDX={arrIDX}, array.Length={array.Length}");
                    return 0;
                }

                int value = array[arrIDX];
                return (value >> bitPosition) & 1;
            }

            public static void SetBitValue(ref int[] array, int x, int bitValue)
            {
                int arrIDX = x / 32;
                int bitPosition = x % 32;

                if (arrIDX >= array.Length)
                {
                    Console.WriteLine($"[ERROR] SetBitValue: Invalid array index. x={x}, arrIDX={arrIDX}, array.Length={array.Length}");
                    return;
                }

                if (bitValue != 0 && bitValue != 1)
                    throw new ArgumentException("Invalid bit value. Only 0 or 1 are allowed.");

                int value = array[arrIDX];
                int mask = 1 << bitPosition;

                if (bitValue == 1)
                    array[arrIDX] = value | mask;
                else
                    array[arrIDX] = value & ~mask;
            }
        }

        public async Task ExecuteDailyQuestsAsync(List<short> questIdList)
        {
            var progressList = await _context.CharacterProgress
                .AsNoTracking()
                .ToListAsync();

            progressList.ForEach(progress =>
            {
                questIdList.ForEach(questId =>
                {
                    progress.CompletedDataValue = MarkQuestIncomplete(questId, progress.CompletedDataValue);
                });

                _context.Update(progress);
            });

            _context.SaveChanges();
        }

        public int[] MarkQuestIncomplete(int qIDX, int[] CompleteDataInt)
        {
            int bitIndex = qIDX - 1;
            int requiredArrayLength = (bitIndex / 32) + 1;

            if (CompleteDataInt.Length < requiredArrayLength)
            {
                Console.WriteLine($"[INFO] Resizing CompletedDataInt from {CompleteDataInt.Length} to {requiredArrayLength}");
                Array.Resize(ref CompleteDataInt, requiredArrayLength);
            }

            int bitValue = BitwiseOperations.GetBitValue(CompleteDataInt, bitIndex);

            if (bitValue == 1)
            {
                BitwiseOperations.SetBitValue(ref CompleteDataInt, bitIndex, 0);
            }

            return CompleteDataInt;
        }

        public async Task ExecuteDailyRewardsAsync()
        {
            try
            {
                var accounts = await _context.Account.ToListAsync();

                accounts.ForEach(account =>
                {
                    account.DailyRewardClaimed = false;
                    _context.Update(account);
                });

                _context.SaveChanges();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<List<RoutineDTO>> GetActiveRoutinesAsync()
        {
            return await _context.Routine
                .AsNoTracking()
                .Where(x => x.Active)
                .ToListAsync();
        }

        public async Task UpdateRoutineExecutionTimeAsync(RoutineTypeEnum routineType)
        {
            var dto = await _context.Routine
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Type == routineType);

            if (dto != null)
            {
                dto.NextRunTime = DateTime.Now.Date.AddDays(dto.Interval);
                _context.Update(dto);
                _context.SaveChanges();
            }
        }
    }
}
