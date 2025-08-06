using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    // Handler para procesar el query GetDigimonByTamerId
    public class GetDigimonByTamerIdQueryHandler : IRequestHandler<GetDigimonByTamerIdQuery, DigimonDTO?>
    {
        private readonly ICharacterQueriesRepository _repository;

        public GetDigimonByTamerIdQueryHandler(ICharacterQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<DigimonDTO?> Handle(GetDigimonByTamerIdQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetDigimonByTamerIdAsync(request.CharacterId);
        }
    }
}