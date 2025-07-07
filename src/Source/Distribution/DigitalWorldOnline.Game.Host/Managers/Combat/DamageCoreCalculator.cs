//Source\Distribution\DigitalWorldOnline.Game.Host\Managers\Combat\DamageCoreCalculator.cs


using System;

namespace DigitalWorldOnline.Game.Managers
{
    /// <summary>
    /// Núcleo único para calcular daño final de ataques y habilidades.
    /// Respeta defensa y permite ignorarla si se indica.
    /// </summary>
    public static class DamageCoreCalculator
    {
        /// <summary>
        /// Calcula el daño final considerando bonificadores de atributo, elemento, nivel y defensa.
        /// </summary>
        /// <param name="baseDamage">Daño base del ataque o habilidad.</param>
        /// <param name="attributeBonus">Bonificador de atributo.</param>
        /// <param name="elementBonus">Bonificador elemental.</param>
        /// <param name="levelBonus">Bonificador por nivel (usado en ataques normales).</param>
        /// <param name="enemyDefence">Defensa total del objetivo.</param>
        /// <param name="ignoreDefence">Si true, ignora la defensa.</param>
        /// <returns>Daño final calculado, mínimo 0.</returns>
        public static int Calculate(
            double baseDamage,
            double attributeBonus,
            double elementBonus,
            double levelBonus,
            double enemyDefence,
            bool ignoreDefence = false)
        {
        double total = baseDamage + attributeBonus + elementBonus + levelBonus;
                if (!ignoreDefence)
                {
                    if (enemyDefence > 3000) enemyDefence = 3000;
                    total -= enemyDefence;
                }
                return (int)Math.Max(0, Math.Floor(total));
        }
    }
}
