using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;
using System;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.AccountBlock
{
    public partial class AccountBlockDelete
    {
        bool _loading;
        long _id;

        [Parameter]
        public string AccountBlockId { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (long.TryParse(AccountBlockId, out _id))
            {
                var result = await Sender.Send(new GetAccountBlockByIdQuery(_id));

                if (result.Register != null)
                    _id = result.Register.Id;
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

            if (firstRender)
            {
                await Delete();
            }
        }

        private async Task Delete()
        {
            try
            {
                _loading = true;
                StateHasChanged();

                Logger.Information("Deleting AccountBlock config id {id}", _id);

                await Sender.Send(new DeleteAccountBlockCommand(_id));

                Logger.Information("AccountBlock config id {id} deleted.", _id);

                Toast.Add("AccountBlock config deleted.", Severity.Success);
            }
            catch (Exception ex)
            {
                Logger.Error("Error deleting AccountBlock config id {id}: {ex}", _id, ex.Message);
                Toast.Add("Unable to delete AccountBlock config, try again later.", Severity.Error);
            }
            finally
            {
                _loading = false;
                StateHasChanged();

                Return();
            }
        }

        private void Return()
        {
            Nav.NavigateTo($"/accountBlock");
        }
    }
}