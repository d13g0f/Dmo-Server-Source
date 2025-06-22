using DigitalWorldOnline.Commons.DTOs.Account;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateAccountBlockCommand : IRequest
    {
        public AccountBlockDTO AccountBlock { get; }

        public UpdateAccountBlockCommand(AccountBlockDTO accountBlock)
        {
            AccountBlock = accountBlock;
        }
    }
}