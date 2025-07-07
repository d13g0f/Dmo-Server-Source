namespace DigitalWorldOnline.Game.Models
{
    /// <summary>
    /// Represents a single buff or debuff applied by a skill.
    /// </summary>
    public class BuffEffect
    {
        public uint BuffId { get; set; }
        public int SkillId { get; set; }
        public int Duration { get; set; }
        public bool IsDebuff { get; set; }
        /// <summary>
        /// Target of this effect: either a ConfigMob (enemy) or the player's Digimon.
        /// </summary>
        public object Target { get; set; }
    }
}
