using System;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Game.Models;

namespace DigitalWorldOnline.Game.Managers
{
    /// <summary>
    /// Resultado detallado del cálculo de daño de un ataque o skill.
    /// </summary>
    public class DamageResult
    {
        /// <summary>
        /// Daño final calculado. Siempre usar esta propiedad.
        /// </summary>
        public int Damage { get; set; }

        /// <summary>
        /// Nombre de la skill usada, si aplica. Null para ataques básicos.
        /// </summary>
        public string? SkillName { get; set; }

        /// <summary>
        /// True si fue golpe crítico.
        /// </summary>
        public bool IsCritical { get; set; }

        /// <summary>
        /// True si fue bloqueado.
        /// </summary>
        public bool IsBlocked { get; set; }

        /// <summary>
        /// True si el ataque falló (evasión).
        /// </summary>
        public bool IsMiss { get; set; }

        /// <summary>
        /// Buffs aplicados junto al golpe. Null si no aplica.
        /// </summary>
        public BuffEffect[]? Buffs { get; set; }
    }
}
