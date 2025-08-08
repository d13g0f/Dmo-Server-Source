using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Digimon;

namespace DigitalWorldOnline.Commons.Extensions
{
    public static class LocationExtensions
    {
        public static void NewLocation(this CharacterDTO character, short mapId, int x, int y)
        {
            if (character.Location == null)
            {
                character.Location = new CharacterLocationDTO();
            }

            character.Location.MapId = mapId;
            character.Location.X = x;
            character.Location.Y = y;
        }

        public static void NewLocation(this DigimonDTO digimon, short mapId, int x, int y)
        {
            if (digimon.Location == null)
            {
                digimon.Location = new DigimonLocationDTO();
            }

            digimon.Location.MapId = mapId;
            digimon.Location.X = x;
            digimon.Location.Y = y;
        }
    }
}