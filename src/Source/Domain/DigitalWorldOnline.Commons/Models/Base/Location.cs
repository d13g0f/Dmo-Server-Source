namespace DigitalWorldOnline.Commons.Models
{
    public partial class Location
    {
        /// <summary>
        /// Reference ID to map.
        /// </summary>
        public short MapId { get;  set; }

        /// <summary>
        /// Position X (horizontal).
        /// </summary>
        public int X { get;  set; }

        /// <summary>
        /// Position Y (vertical).
        /// </summary>
        public int Y { get;  set; }

        /// <summary>
        /// Position Z (looking for).
        /// </summary>
        public float Z { get;  set; }

        public int TicksCount { get; private set; }
    }
}