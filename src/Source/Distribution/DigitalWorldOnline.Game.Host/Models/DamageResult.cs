namespace DigitalWorldOnline.Game.Models
{
    /// <summary>
    /// Holds the raw parts of a skill damage calculation for possible AoE splitting.
    /// </summary>
    public class DamageResult
    {
        /// <summary>
        /// Parte base del skill (escala con nivel, clone, SCD, buffs de skill).
        /// </summary>
        public double SkillPortion { get; set; }

        /// <summary>
        /// Parte que viene de AT y SKD.
        /// </summary>
        public double AttackPortion { get; set; }

        /// <summary>
        /// Nombre para log.
        /// </summary>
        public required string SkillName { get; set; }

        /// <summary>
        /// Daño final, si es single target o ya procesado.
        /// </summary>
        public int FinalDamage { get; set; }

        /// <summary>
        /// Buffs aplicados.
        /// </summary>
        public BuffEffect[] Buffs { get; set; } = new BuffEffect[0];
    }
}
