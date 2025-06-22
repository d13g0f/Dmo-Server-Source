namespace DigitalWorldOnline.Commons.Models.Chat
{
    public sealed partial class CommandMessageModel
    {
        /// <summary>
        /// Unique sequential identifier.
        /// </summary>
        public long Id { get; private set; }

        /// <summary>
        /// Sent date.
        /// </summary>
        public DateTime Time { get; private set; }

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Owner id.
        /// </summary>
        public long CharacterId { get; private set; }

        /// <summary>
        /// Owner Name.
        /// </summary>
        public string CharacterName { get; private set; }

        public CommandMessageModel()
        {
            Time = DateTime.Now;
        }
    }
}