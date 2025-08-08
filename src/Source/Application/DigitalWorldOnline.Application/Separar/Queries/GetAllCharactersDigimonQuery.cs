using MediatR;
using DigitalWorldOnline.Commons.DTOs.Digimon;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    public class GetAllCharactersDigimonQuery : IRequest<List<DigimonDTO>>
    {
        public int Skip { get; }
        public int Take { get; }

        public GetAllCharactersDigimonQuery(int skip = 0, int take = 100)
        {
            Skip = skip;
            Take = take;
        }
    }
}