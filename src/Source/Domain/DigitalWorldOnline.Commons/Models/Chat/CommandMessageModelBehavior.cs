namespace DigitalWorldOnline.Commons.Models.Chat
{
    public sealed partial class CommandMessageModel
    {
        /// <summary>
        /// Creates a new chat message object.
        /// </summary>
        /// <param name="characterId">Ownner id.</param>
        /// <param name="message">Message.</param>
        /// <param name="tamerName">Ownner name.</param>
        public static CommandMessageModel Create(long characterId, string message, string tamerName)
        {
            return new CommandMessageModel()
            {
                CharacterId = characterId,
                Message = message,
                CharacterName = tamerName
            };
        }
    }
}