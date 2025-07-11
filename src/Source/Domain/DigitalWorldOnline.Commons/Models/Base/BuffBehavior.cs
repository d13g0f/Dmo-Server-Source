using DigitalWorldOnline.Commons.Models.Asset;
using System;
using System.IO;

namespace DigitalWorldOnline.Commons.Models
{
    public partial class Buff
    {
        public bool Expired => Duration == -1 || (Duration > 0 && DateTime.Now.AddSeconds(3) >= EndDate);
        public bool DebuffExpired => DateTime.Now >= EndDate;


        public int RemainingSeconds => (EndDate - DateTime.Now).TotalSeconds > 0 ? (int)(EndDate - DateTime.Now).TotalSeconds : 0;

        public void IncreaseDuration(int duration)
        {
            Duration += duration;
            EndDate = DateTime.Now.AddSeconds(Duration);
        }

        public void IncreaseEndDate(int duration)
        {
            Duration = duration;
            EndDate = DateTime.Now.AddSeconds(duration);
        }

        public static Buff Create(int buffId, int skillId, int duration = 0)
        {
            return new Buff
            {
                BuffId = buffId,
                SkillId = skillId,
                Duration = duration,
                EndDate = DateTime.Now.AddSeconds(duration)
            };
        }

        // LEGACY: solo para el sistema viejo
        public void SetBuffInfo(BuffInfoAssetModel? buffInfo) => BuffInfo ??= buffInfo;

        // NUEVO: solo para buffs JSON-driven
        public void SetBuffInfoFromJson(DigimonBuffJsonModel buffJson)
        {
            BuffId = buffJson.BuffId;
            SkillId = buffJson.SkillCode;
            BuffInfoJson = buffJson;
        }


        public DigimonBuffJsonModel? BuffInfoJson { get; private set; }

        public void SetBuffId(int buffId) => BuffId = buffId;
        public void SetTypeN(int typeN) => TypeN = typeN;

        public void SetCooldown(int cooldown)
        {
            Cooldown = cooldown;
            CoolEndDate = DateTime.Now.AddSeconds(cooldown);
        }

        public void SetSkillId(int skillId) => SkillId = skillId;

        public void SetDuration(int duration, bool fixedValue = false)
        {
            if (fixedValue)
            {
                Duration = duration;
                EndDate = DateTime.UtcNow.AddSeconds(Duration);
            }
            else
            {
                Duration += duration;
                EndDate = EndDate.AddSeconds(duration);
            }
        }

        public void SetEndDate(DateTime endDate) => EndDate = endDate;

        public byte[] ToArray()
        {
            using MemoryStream m = new();
            m.Write(BitConverter.GetBytes(BuffId), 0, 4);
            m.Write(BitConverter.GetBytes(Duration), 0, 4);
            m.Write(BitConverter.GetBytes(SkillId), 0, 4);
            return m.ToArray();
        }
    }
}
