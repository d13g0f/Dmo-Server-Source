using DigitalWorldOnline.Commons.Models.Servers;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.MapServer
{
    public class ServerExperiencePacket : PacketWriter
    {
        private const int PacketNumber = 1054;

        /// <summary>
        /// Load the server experience bonus.
        /// </summary>
        /// <param name="server">The experience information to load</param>
        public ServerExperiencePacket(ServerObject server)
        {
            var serverExp = server.Experience;

            if (server.ExperienceType == 1)
            {
                serverExp = server.Experience;
            }
            else if (server.ExperienceType == 2)
            {
                serverExp = server.ExperienceBurn;
            }

            Type(PacketNumber);
            WriteInt(1);                        // Event Type
            WriteInt(serverExp);                // Experience rate
            WriteInt(server.ExperienceType);    // Experience type 1: Normal 2: Burn
            WriteInt(500);                      // If the user is subject to special experience points, it contains a non-zero value. The maximum is not more than 500
            WriteInt(100);                      // Experience percentage received when logging in the next day
        }
    }
}