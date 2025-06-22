using DigitalWorldOnline.Commons.DTOs.Server;
using DigitalWorldOnline.Commons.Enums.Server;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class CreateServerCommand : IRequest<ServerDTO>
    {
        public string Name { get; }
        public int Experience { get; }
        public int ExperienceBurn { get; }
        public int ExperienceType { get; }
        public ServerTypeEnum Type { get; }
        public int Port { get; }

        public CreateServerCommand(string name, int experience, int experienceBurn, int experienceType, ServerTypeEnum type, int port)
        {
            Name = name;
            Experience = experience;
            ExperienceBurn = experienceBurn;
            ExperienceType = experienceType;
            Type = type;
            Port = port;
        }
    }
}