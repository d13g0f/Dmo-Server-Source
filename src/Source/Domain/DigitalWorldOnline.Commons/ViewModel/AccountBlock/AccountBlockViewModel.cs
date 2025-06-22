using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.Enums.Account;

namespace DigitalWorldOnline.Commons.ViewModel.AccountBlock
{
    public class AccountBlockViewModel
    {
        /// <summary>
        /// Sequencial unique identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// AccountBlock type.
        /// </summary>
        public AccountBlockEnum Type { get; set; }

        /// <summary>
        /// Reason for AccountBlock.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// StartDate of block.
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// EndDate of block.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Blocked Account Id.
        /// </summary>
        public long AccountId { get; set; }

        public AccountDTO Account { get; set; }
    }
}
