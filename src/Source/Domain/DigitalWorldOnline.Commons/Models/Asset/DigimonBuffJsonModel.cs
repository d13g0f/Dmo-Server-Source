using DigitalWorldOnline.Commons.Enums;

namespace DigitalWorldOnline.Commons.Models.Asset

{

    public class DigimonBuffJsonModel
    {
        public int BuffId { get; set; }
        public string Name { get; set; }
        public BuffEffectTypeEnum EffectType { get; set; }
        public int SkillCode { get; set; }
        public int Value { get; set; }
        public int Chance { get; set; }
        public int DurationMs { get; set; }
        public int TickIntervalMs { get; set; }
        public bool Stackable { get; set; }
        public int MaxStacks { get; set; }
        public bool Dispellable { get; set; }
        public bool AreaOfEffect { get; set; }
        
    }



}




