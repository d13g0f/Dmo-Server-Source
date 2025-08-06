using DigitalWorldOnline.Commons.DTOs.Digimon;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    // Query para obtener el Digimon activo por CharacterId
    public class GetDigimonByTamerIdQuery : IRequest<DigimonDTO?>
    {
        public long CharacterId { get; }

        public GetDigimonByTamerIdQuery(long characterId)
        {
            CharacterId = characterId;
        }
    }
}