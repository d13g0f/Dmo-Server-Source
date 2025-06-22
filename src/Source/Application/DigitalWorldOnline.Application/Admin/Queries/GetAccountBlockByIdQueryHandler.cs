using MediatR;
using DigitalWorldOnline.Application.Admin.Repositories;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetAccountBlockByIdQueryHandler : IRequestHandler<GetAccountBlockByIdQuery, GetAccountBlockByIdQueryDto>
    {
        private readonly IAdminQueriesRepository _repository;

        public GetAccountBlockByIdQueryHandler(IAdminQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetAccountBlockByIdQueryDto> Handle(GetAccountBlockByIdQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetAccountBlockByIdAsync(request.Id);
        }
    }
}