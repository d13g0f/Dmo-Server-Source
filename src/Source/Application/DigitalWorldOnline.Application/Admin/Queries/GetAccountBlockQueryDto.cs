using DigitalWorldOnline.Commons.DTOs.Account;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetAccountBlockQueryDto
    {
        public int TotalRegisters { get; set; }
        public List<AccountBlockDTO> Registers { get; set; }
    }
}