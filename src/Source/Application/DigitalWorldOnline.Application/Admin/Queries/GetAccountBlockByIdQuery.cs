using MediatR;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetAccountBlockByIdQuery : IRequest<GetAccountBlockByIdQueryDto>
    {
        public long Id { get; }

        public GetAccountBlockByIdQuery(long id)
        {
            Id = id;
        }
    }
}