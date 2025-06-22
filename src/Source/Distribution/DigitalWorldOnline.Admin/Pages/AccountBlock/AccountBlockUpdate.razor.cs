using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.ViewModel.AccountBlock;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;
using System;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.AccountBlock
{
    public partial class AccountBlockUpdate
    {
        AccountBlockViewModel _accountBlock = new AccountBlockViewModel();
        bool Loading = false;
        long _id;

        [Parameter]
        public string AccountBlockId { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (long.TryParse(AccountBlockId, out _id))
            {
                Logger.Information("Searching AccountBlock config by id {id}", _id);

                var target = await Sender.Send(
                    new GetAccountBlockByIdQuery(_id)
                );

                if (target.Register == null)
                    _id = 0;
                else
                {
                    _accountBlock = Mapper.Map<AccountBlockViewModel>(target.Register);
                }
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0)
            {
                Logger.Information("Invalid AccountBlock config id parameter: {parameter}", AccountBlockId);
                Toast.Add("AccountBlock config not found, try again later.", Severity.Warning);

                Return();
            }

            StateHasChanged();
        }

        private async Task Update()
        {
            try
            {
                //if (_accountBlock.Invalid)
                //{
                //    Toast.Add("Invalid AccountBlock configuration.", Severity.Warning);

                //    return;
                //}

                Loading = true;

                StateHasChanged();

                Logger.Information("Updating AccountBlock config id {id}", _accountBlock.Id);

                var configDto = Mapper.Map<AccountBlockDTO>(_accountBlock);

                await Sender.Send(new UpdateAccountBlockCommand(configDto));

                Logger.Information("AccountBlock config id {id} update", _accountBlock.Id);

                Toast.Add("AccountBlock updated successfully.", Severity.Success);

                Return();
            }
            catch (Exception ex)
            {
                Logger.Error("Error updating AccountBlock config with id {id}: {ex}", _accountBlock.Id, ex.Message);
                Toast.Add("Unable to update AccountBlock config, try again later.", Severity.Error);
                Return();
            }
            finally
            {
                Loading = false;

                StateHasChanged();
            }
        }

        private void Return()
        {
            Nav.NavigateTo($"/accountBlock");
        }
    }
}