
namespace DigitalWorldOnline.Commons.Models.Digimon
{
    public class RootDigicloneConfig
    {
        public List<DigicloneConfigSection> Digiclones { get; set; } = new();
        public List<DigicloneLevelStat> CloneLevels { get; set; } = new();
        
    }

    public class DigicloneConfigSection
    {
        public int[] SectionRange { get; set; }
        public string Name { get; set; } = "";
        public List<DigicloneGradeConfig> Grades { get; set; } = new();
    }

    public class DigicloneGradeConfig
    {
        public string Grade { get; set; } = "";
        public double SuccessChance { get; set; }
        public double RetentionRate { get; set; }
        public double DowngradeRate { get; set; }
        public int MinValue { get; set; }
        public int MaxValue { get; set; }
    }

    public class DigicloneLevelStat
    {
        public int Level { get; set; }
        public double AT { get; set; }
        public double CT { get; set; }
        public double BL { get; set; }
        public double EV { get; set; }
        public double HP { get; set; }
    }

}