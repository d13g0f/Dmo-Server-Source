namespace DigitalWorldOnline.Commons.Enums
{
    public enum BuffEffectTypeEnum
    {
        Unknown = 0,
        DoT = 1,          // Damage over Time
        HoT = 2,          // Heal over Time
        Shield = 3,       // Absorb damage
        Stun = 4,         // Crowd control
        StatBoost = 5,    // Increase stat
        StatReduce = 6,   // Decrease stat
        Reflect = 7,      // Reflect damage
        Unbeatable = 8,   // Invincible
        Slow = 9,         // Movement/attack speed down
        AoE = 10,          // Area of Effect
        SkillDmg = 11, // Increase skill damage
    }
}
