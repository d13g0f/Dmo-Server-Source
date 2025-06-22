using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchFinishPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchFinish;

        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public HatchFinishPacketProcessor(StatusManager statusManager, AssetsLoader assets,
            MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer,
            ILogger logger, ISender sender, IMapper mapper)
        {
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            packet.Skip(5);
            var digiName = packet.ReadString();

            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == client.Tamer.Incubator.EggId);

            if (hatchInfo == null)
            {
                _logger.Warning($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}.");
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}."));
                return;
            }


            /*byte i = 0;
            while (i < client.Tamer.DigimonSlots)
            {
                if (client.Tamer.Digimons.FirstOrDefault(x => x.Slot == i) == null)
                    break;

                i++;
            }*/

            byte? digimonSlot = (byte)Enumerable.Range(0, client.Tamer.DigimonSlots)
                .FirstOrDefault(slot => client.Tamer.Digimons.FirstOrDefault(x => x.Slot == slot) == null);

            if (digimonSlot == null)
                return;

            var newDigimon = DigimonModel.Create(
                digiName,
                hatchInfo.HatchType,
                hatchInfo.HatchType,
                (DigimonHatchGradeEnum)client.Tamer.Incubator.HatchLevel,
                client.Tamer.Incubator.GetLevelSize(),
                (byte)digimonSlot
            );

            newDigimon.NewLocation(
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            );

            newDigimon.SetBaseInfo(
                _statusManager.GetDigimonBaseInfo(
                    newDigimon.BaseType
                )
            );

            newDigimon.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(
                    newDigimon.BaseType,
                    newDigimon.Level,
                    newDigimon.Size
                )
            );

            var digimonEvolutionInfo = _assets.EvolutionInfo.First(x => x.Type == newDigimon.BaseType);

            newDigimon.AddEvolutions(digimonEvolutionInfo);

            if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null || !newDigimon.Evolutions.Any())
            {
                _logger.Warning($"Unknown digimon info for {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                return;
            }

            newDigimon.SetTamer(client.Tamer);

            if (client.Tamer.Incubator.PerfectSize(newDigimon.HatchGrade, newDigimon.Size))
            {
                client.SendToAll(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name,
                    newDigimon.BaseType, newDigimon.Size).Serialize());
                /*_mapServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
                _dungeonServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
                _eventServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
                _pvpServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());*/
            }
            
            var digimonInfo = _mapper.Map<DigimonModel>(await _sender.Send(new CreateDigimonCommand(newDigimon)));
            client.Tamer.AddDigimon(digimonInfo);
            client.Tamer.Incubator.RemoveEgg();
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));

            client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000),
                (byte)digimonSlot));

            if (digimonInfo != null)
            {
                newDigimon.SetId(digimonInfo.Id);
                var slot = -1;

                foreach (var digimon in newDigimon.Evolutions)
                {
                    slot++;

                    var evolution = digimonInfo.Evolutions[slot];

                    if (evolution != null)
                    {
                        digimon.SetId(evolution.Id);

                        var skillSlot = -1;

                        foreach (var skill in digimon.Skills)
                        {
                            skillSlot++;

                            var dtoSkill = evolution.Skills[skillSlot];

                            skill.SetId(dtoSkill.Id);
                        }
                    }
                }
            }

            _logger.Verbose(
                $"Character {client.TamerId} hatched {newDigimon.Id}({newDigimon.BaseType}) with grade {newDigimon.HatchGrade} and size {newDigimon.Size}.");

            // ------------------------------------------------------------------------------------------

        var digimonBaseInfo = newDigimon.BaseInfo;
        var digimonEvolutions = newDigimon.Evolutions;

        var encyclopediaExists = client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo?.Id);

        if (encyclopediaExists)
        {
            // La enciclopedia ya existe, actualizamos las evoluciones
            var existingEncyclopedia = client.Tamer.Encyclopedia.First(x => x.DigimonEvolutionId == digimonEvolutionInfo?.Id);
            
            digimonEvolutions?.ForEach(x =>
            {
                var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);
                byte slotLevel = evolutionLine != null ? evolutionLine.SlotLevel : (byte)0;

                var existingEvolution = existingEncyclopedia.Evolutions.FirstOrDefault(e => e.DigimonBaseType == x.Type);
                if (existingEvolution == null)
                {
                    // Si la evolución no está en la enciclopedia, la añadimos
                    existingEncyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(
                        existingEncyclopedia.Id, x.Type, slotLevel, Convert.ToBoolean(x.Unlocked)));
                    _logger.Information($"[HatchFinishPacketProcessor] Añadida nueva evolución DigimonBaseType: {x.Type} para EncyclopediaId: {existingEncyclopedia.Id}, IsUnlocked: {x.Unlocked}");
                }
                else if (Convert.ToBoolean(x.Unlocked) && !existingEvolution.IsUnlocked)
                {
                    // Si la evolución existe pero ahora está desbloqueada, la reemplazamos
                    existingEncyclopedia.Evolutions.Remove(existingEvolution);
                    existingEncyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(
                        existingEncyclopedia.Id, x.Type, slotLevel, true));
                    _logger.Information($"[HatchFinishPacketProcessor] Reemplazada evolución DigimonBaseType: {x.Type} con IsUnlocked: true para EncyclopediaId: {existingEncyclopedia.Id}");
                }
            });

            // Ejecutamos el comando de actualización
            await _sender.Send(new UpdateCharacterEncyclopediaCommand(existingEncyclopedia));
            _logger.Information($"[HatchFinishPacketProcessor] Enciclopedia actualizada para TamerId: {client.TamerId}, DigimonEvolutionId: {digimonEvolutionInfo?.Id}");
        }
        else
        {
            // La enciclopedia no existe, creamos una nueva entrada
            if (digimonEvolutionInfo != null)
            {
                var encyclopedia = CharacterEncyclopediaModel.Create(client.TamerId, digimonEvolutionInfo.Id,
                    newDigimon.Level, newDigimon.Size, 0, 0, 0, 0, 0, false, false);

                digimonEvolutions?.ForEach(x =>
                {
                    var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);
                    byte slotLevel = evolutionLine != null ? evolutionLine.SlotLevel : (byte)0;

                    encyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(
                        encyclopedia.Id, x.Type, slotLevel, Convert.ToBoolean(x.Unlocked)));
                    _logger.Information($"[HatchFinishPacketProcessor] Añadida evolución DigimonBaseType: {x.Type} a nueva enciclopedia para TamerId: {client.TamerId}, IsUnlocked: {x.Unlocked}");
                });

                var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));
                client.Tamer.Encyclopedia.Add(encyclopediaAdded);
                _logger.Information($"[HatchFinishPacketProcessor] Enciclopedia creada para TamerId: {client.TamerId}, DigimonEvolutionId: {digimonEvolutionInfo?.Id}, EncyclopediaId: {encyclopediaAdded.Id}");
            }
        }
            _logger.Debug(
                $"Hatching Leveling status for character {client.Tamer.Name} is: {client.Tamer.LevelingStatus?.Id}");
            _logger.Debug(
                $"Hatching Leveling status in digimon for character {newDigimon.Character.Name} is: {newDigimon.Character.LevelingStatus?.Id}");
        }
    }
}