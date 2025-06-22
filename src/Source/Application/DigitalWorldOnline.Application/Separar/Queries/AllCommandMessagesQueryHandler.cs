using DigitalWorldOnline.Commons.DTOs.Chat;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    public class AllCommandMessagesQueryHandler : IRequestHandler<AllCommandMessagesQuery, IList<CommandMessageDTO>>
    {
        private readonly IServerQueriesRepository _repository;

        public AllCommandMessagesQueryHandler(IServerQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<IList<CommandMessageDTO>> Handle(AllCommandMessagesQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetAllCommandMessagesAsync();
        }
    }
}
