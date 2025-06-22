using MediatR;
using DigitalWorldOnline.Application.Admin.Repositories;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetAccountBlockQueryHandler : IRequestHandler<GetAccountBlockQuery, GetAccountBlockQueryDto>
    {
        private readonly IAdminQueriesRepository _repository;

        public GetAccountBlockQueryHandler(IAdminQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetAccountBlockQueryDto> Handle(GetAccountBlockQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetAccountBlockAsync(request.Limit, request.Offset, request.SortColumn, request.SortDirection, request.Filter);
        }
    }
}