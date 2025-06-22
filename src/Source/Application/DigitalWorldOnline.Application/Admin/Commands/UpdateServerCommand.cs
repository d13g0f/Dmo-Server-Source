using DigitalWorldOnline.Commons.Enums.Server;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateServerCommand : IRequest
    {
        public long Id { get; }
        public string Name { get; }
        public int Experience { get; }
        public int ExperienceBurn { get; }
        public int ExperienceType { get; }
        public bool Maintenance { get; }
        public ServerTypeEnum Type { get; }
        public int Port { get; }

        public UpdateServerCommand(long id, string name,
            int experience, int experienceBurn, int experienceType,
            bool maintenance, ServerTypeEnum type, int port)
        {
            Id = id;
            Name = name;
            Experience = experience;
            ExperienceBurn = experienceBurn;
            ExperienceType = experienceType;
            Maintenance = maintenance;
            Type = type;
            Port = port;
        }
    }
}