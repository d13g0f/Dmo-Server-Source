using System.Text.Json.Serialization;

namespace DigitalWorldOnline.Commons.Models.Asset
{

public class DigimonSkillJsonModel
{
     [JsonPropertyName("SkillId")]
        public int SkillId { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("BaseDamage")]
        public int BaseDamage { get; set; }

        [JsonPropertyName("DamagePerLevel")]
        public int DamagePerLevel { get; set; }

        [JsonPropertyName("LevelUpPoint")]
        public int LevelUpPoint { get; set; }

        [JsonPropertyName("MaxLevel")]
        public int MaxLevel { get; set; }

        [JsonPropertyName("AttributeType")]
        public int AttributeType { get; set; }

        [JsonPropertyName("NatureType")]
        public int NatureType { get; set; }

        [JsonPropertyName("FamilyType")]
        public int FamilyType { get; set; }

        [JsonPropertyName("HPUsage")]
        public int HPUsage { get; set; }

        [JsonPropertyName("DSUsage")]
        public int DSUsage { get; set; }

        [JsonPropertyName("Target")]
        public int Target { get; set; }

        [JsonPropertyName("AttackType")]
        public int AttackType { get; set; }

        [JsonPropertyName("Range")]
        public int Range { get; set; }

        [JsonPropertyName("CastingTime")]
        public float CastingTime { get; set; }

        [JsonPropertyName("CooldownMs")]
        public int CooldownMs { get; set; }

        [JsonPropertyName("UnlockLevel")]
        public int UnlockLevel { get; set; }

        [JsonPropertyName("IconId")]
        public int IconId { get; set; }

        [JsonPropertyName("BuffCode")]
        public int BuffCode { get; set; }
}


}