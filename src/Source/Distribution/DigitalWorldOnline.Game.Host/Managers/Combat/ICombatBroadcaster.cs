using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Game.Models;
using DigitalWorldOnline.Commons.Entities;

namespace DigitalWorldOnline.Game.Managers.Combat
{
    /// <summary>
    /// Encapsula la lógica para emitir eventos de combate:
    /// daños, buffs y debuffs hacia el resto de jugadores.
    /// </summary>
    public interface ICombatBroadcaster
    {
        /// <summary>
        /// Difunde todos los paquetes de combate relacionados:
        /// mensaje de daño y efectos de buff/debuff.
        /// </summary>
        /// <param name="client">Cliente que ejecutó la skill.</param>
        /// <param name="damageResult">Resultado de la fórmula de daño.</param>
        /// <param name="buffEffects">Efectos de buff/debuff que deben transmitirse.</param>
        void BroadcastCombat(GameClient client, DamageResult damageResult, BuffEffect[] buffEffects);
    }
}
