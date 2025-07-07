namespace DigitalWorldOnline.Game.Models
{
    /// <summary>
    /// Holds the result of a skill damage calculation, including any buff/debuff effects.
    /// </summary>
    public class DamageResult
    {
        public int FinalDamage { get; set; }
        public required string SkillName { get; set; }
        public BuffEffect[] Buffs { get; set; } = new BuffEffect[0];
    }
}
