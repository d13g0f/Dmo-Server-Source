using DigitalWorldOnline.Commons.DTOs.Character;

namespace DigitalWorldOnline.Commons.DTOs.Chat
{
    public sealed class CommandMessageDTO
    {
        public long Id { get; private set; }
        public DateTime Time { get; private set; }
        public string Message { get; private set; }

        public CharacterDTO Character { get; private set; }
        public long CharacterId { get; private set; }
        public string CharacterName { get; private set; }
    }
}