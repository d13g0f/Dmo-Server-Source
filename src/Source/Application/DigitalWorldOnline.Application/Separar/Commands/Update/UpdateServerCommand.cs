using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateServerCommand : IRequest
    {
        public long ServerId { get; set; }
        public string Name { get; set; }
        public int Experience { get; set; }
        public int ExperienceBurn { get; set; }
        public int ExperienceType { get; set; }
        public bool Maintenance { get; set; }

        public UpdateServerCommand(long serverId, string name, int experience, bool maintenance, int experienceBurn, int experienceType)
        {
            ServerId = serverId;
            Name = name;
            Experience = experience;
            Maintenance = maintenance;
            ExperienceBurn = experienceBurn;
            ExperienceType = experienceType;
        }
    }
}
