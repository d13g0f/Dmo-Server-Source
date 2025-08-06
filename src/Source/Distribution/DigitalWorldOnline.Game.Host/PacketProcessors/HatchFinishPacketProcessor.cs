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
using GameServer.Logging;
using MediatR;

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
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public HatchFinishPacketProcessor(
            StatusManager statusManager,
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ISender sender,
            IMapper mapper)
        {
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _mapper = mapper;
        }

        // Processes the packet to finalize egg hatching, creating a new Digimon.
        // Includes explicit validations and detailed logging in Spanish to diagnose issues.
        // Sends English error messages to the client.
        public async Task Process(GameClient client, byte[] packetData)
        {
            // Log start of packet processing
            await GameLogger.LogInfo($"Inicio del procesamiento del paquete HatchFinish para TamerId={client.TamerId}", "hatch");

            var packet = new GamePacketReader(packetData);
            packet.Skip(5);
            var digiName = packet.ReadString();

            // Log received packet data
            await GameLogger.LogInfo($"Datos del paquete: DigiName={digiName}, EggId={client.Tamer.Incubator.EggId}, HatchLevel={client.Tamer.Incubator.HatchLevel}", "hatch");

            // Validate hatch info
            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == client.Tamer.Incubator.EggId);
            if (hatchInfo == null)
            {
                await GameLogger.LogWarning($"No se encontró información del huevo para EggId={client.Tamer.Incubator.EggId}", "hatch");
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}."));
                return;
            }
            await GameLogger.LogInfo($"HatchInfo encontrado: HatchType={hatchInfo.HatchType}", "hatch");

            // Find available Digimon slot
            byte? digimonSlot = (byte)Enumerable.Range(0, client.Tamer.DigimonSlots)
                .FirstOrDefault(slot => client.Tamer.Digimons.FirstOrDefault(x => x.Slot == slot) == null);
            if (digimonSlot == null)
            {
                await GameLogger.LogWarning($"No hay slots disponibles para el nuevo Digimon. DigimonSlots={client.Tamer.DigimonSlots}", "hatch");
                client.Send(new SystemMessagePacket("No available slots for new Digimon."));
                return;
            }
            await GameLogger.LogInfo($"Slot disponible encontrado: Slot={digimonSlot}", "hatch");

            // Create new Digimon
            var newDigimon = DigimonModel.Create(
                digiName,
                hatchInfo.HatchType,
                hatchInfo.HatchType,
                (DigimonHatchGradeEnum)client.Tamer.Incubator.HatchLevel,
                client.Tamer.Incubator.GetLevelSize(),
                (byte)digimonSlot
            );
            await GameLogger.LogInfo($"Nuevo Digimon creado: Id={newDigimon.Id}, BaseType={newDigimon.BaseType}, HatchGrade={newDigimon.HatchGrade}, Size={newDigimon.Size}", "hatch");

            // Set Digimon location
            newDigimon.NewLocation(
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            );
            await GameLogger.LogInfo($"Ubicación asignada: MapId={client.Tamer.Location.MapId}, X={client.Tamer.Location.X}, Y={client.Tamer.Location.Y}", "hatch");

            // Set base info
            newDigimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(newDigimon.BaseType));
            if (newDigimon.BaseInfo == null)
            {
                await GameLogger.LogError($"No se encontró BaseInfo para Digimon BaseType={newDigimon.BaseType}", "hatch");
                client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                return;
            }
            await GameLogger.LogInfo($"BaseInfo asignado: BaseType={newDigimon.BaseType}", "hatch");

            // Set base status
            newDigimon.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(
                    newDigimon.BaseType,
                    newDigimon.Level,
                    newDigimon.Size
                )
            );
            if (newDigimon.BaseStatus == null)
            {
                await GameLogger.LogError($"No se encontró BaseStatus para Digimon BaseType={newDigimon.BaseType}, Level={newDigimon.Level}, Size={newDigimon.Size}", "hatch");
                client.Send(new SystemMessagePacket($"Unknown digimon status for {newDigimon.BaseType}."));
                return;
            }
            await GameLogger.LogInfo($"BaseStatus asignado: BaseType={newDigimon.BaseType}, Level={newDigimon.Level}, Size={newDigimon.Size}", "hatch");

            // Set evolutions
            var digimonEvolutionInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == newDigimon.BaseType);
            if (digimonEvolutionInfo == null)
            {
                await GameLogger.LogError($"No se encontró EvolutionInfo para Digimon BaseType={newDigimon.BaseType}", "hatch");
                client.Send(new SystemMessagePacket($"Unknown evolution info for {newDigimon.BaseType}."));
                return;
            }
            await GameLogger.LogInfo($"EvolutionInfo encontrado: Id={digimonEvolutionInfo.Id}", "hatch");

            newDigimon.AddEvolutions(digimonEvolutionInfo);
            if (!newDigimon.Evolutions.Any())
            {
                await GameLogger.LogError($"No se asignaron evoluciones para Digimon BaseType={newDigimon.BaseType}", "hatch");
                client.Send(new SystemMessagePacket($"No evolutions available for {newDigimon.BaseType}."));
                return;
            }
            await GameLogger.LogInfo($"Evoluciones asignadas: Count={newDigimon.Evolutions.Count}", "hatch");

            // Set Tamer
            newDigimon.SetTamer(client.Tamer);
            await GameLogger.LogInfo($"Tamer asignado: TamerId={client.TamerId}", "hatch");

            try
            {
                // Validate incubator and check PerfectSize
                if (client.Tamer?.Incubator == null)
                {
                    await GameLogger.LogError($"Incubadora nula para TamerId={client.TamerId}", "hatch");
                    client.Send(new SystemMessagePacket("Incubator data is missing."));
                    return;
                }

                // Log and check PerfectSize (SendToAll removed)
                await GameLogger.LogInfo($"Iniciando verificación de PerfectSize para TamerId={client.TamerId}, HatchGrade={newDigimon.HatchGrade}, Size={newDigimon.Size}", "hatch");
                
                bool isPerfectSize = client.Tamer.Incubator.PerfectSize(newDigimon.HatchGrade, newDigimon.Size);

                if (isPerfectSize)
                {
                    await GameLogger.LogInfo($"PerfectSize detectado para Tamer={client.Tamer.Name}, BaseType={newDigimon.BaseType}, Size={newDigimon.Size}", "hatch");
                }

                // Persist Digimon to database
                await GameLogger.LogInfo($"Iniciando persistencia de Digimon para TamerId={client.TamerId}, BaseType={newDigimon.BaseType}", "hatch");
                var digimonInfo = _mapper.Map<DigimonModel>(await _sender.Send(new CreateDigimonCommand(newDigimon)));
                if (digimonInfo == null)
                {
                    await GameLogger.LogError($"Fallo al persistir Digimon en la base de datos: BaseType={newDigimon.BaseType}", "hatch");
                    client.Send(new SystemMessagePacket("Failed to create Digimon."));
                    return;
                }
                await GameLogger.LogInfo($"Digimon persistido: Id={digimonInfo.Id}, BaseType={digimonInfo.BaseType}", "hatch");

                // Add Digimon to Tamer and clear incubator
                client.Tamer.AddDigimon(digimonInfo);
                await GameLogger.LogInfo($"Digimon agregado al Tamer: Id={digimonInfo.Id}, Slot={digimonSlot}", "hatch");

                client.Tamer.Incubator.RemoveEgg();
                await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
                await GameLogger.LogInfo($"Huevo removido del incubador: TamerId={client.TamerId}", "hatch");

                // Send HatchFinish packet
                client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000), (byte)digimonSlot));
                await GameLogger.LogInfo($"Paquete HatchFinish enviado: DigimonId={newDigimon.Id}, Slot={digimonSlot}", "hatch");

                // Update Digimon IDs and evolutions
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
                        else
                        {
                            await GameLogger.LogWarning($"Evolución no encontrada para slot={slot}, DigimonId={newDigimon.Id}", "hatch");
                        }
                    }
                    await GameLogger.LogInfo($"Evoluciones actualizadas: DigimonId={newDigimon.Id}, EvolutionCount={newDigimon.Evolutions.Count}", "hatch");
                }

                // Log successful hatch
                await GameLogger.LogInfo($"Tamer {client.TamerId} eclosionó Digimon {newDigimon.Id} (BaseType={newDigimon.BaseType}) con grado {newDigimon.HatchGrade} y tamaño {newDigimon.Size}", "hatch");

                // Handle encyclopedia
                var digimonBaseInfo = newDigimon.BaseInfo;
                var digimonEvolutions = newDigimon.Evolutions;
                var encyclopediaExists = client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo?.Id);

                if (encyclopediaExists)
                {
                    await GameLogger.LogInfo($"Enciclopedia existente: BaseType={newDigimon.BaseType}, EvolutionId={digimonEvolutionInfo?.Id}", "hatch");
                }
                else
                {
                    if (digimonEvolutionInfo != null)
                    {
                        var encyclopedia = CharacterEncyclopediaModel.Create(client.TamerId, digimonEvolutionInfo.Id,
                            newDigimon.Level, newDigimon.Size, 0, 0, 0, 0, 0, false, false);

                        digimonEvolutions?.ForEach(x =>
                        {
                            var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);
                            byte slotLevel = 0;
                            if (evolutionLine != null)
                            {
                                slotLevel = evolutionLine.SlotLevel;
                            }
                            else
                            {
                                _= GameLogger.LogWarning($"EvolutionLine no encontrada para Type={x.Type}, DigimonId={newDigimon.Id}", "hatch");
                            }

                            encyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(encyclopedia.Id, x.Type,
                                slotLevel, Convert.ToBoolean(x.Unlocked)));
                        });

                        var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));
                        if (encyclopediaAdded == null)
                        {
                            await GameLogger.LogError($"Fallo al crear entrada en la enciclopedia: EvolutionId={digimonEvolutionInfo.Id}", "hatch");
                        }
                        else
                        {
                            client.Tamer.Encyclopedia.Add(encyclopediaAdded);
                            await GameLogger.LogInfo($"Entrada en la enciclopedia creada: Id={encyclopediaAdded.Id}, EvolutionId={digimonEvolutionInfo.Id}", "hatch");
                        }
                    }
                    else
                    {
                        await GameLogger.LogError($"No se pudo crear entrada en la enciclopedia: EvolutionInfo nulo para BaseType={newDigimon.BaseType}", "hatch");
                    }
                }

                // Log leveling status
                await GameLogger.LogInfo($"Estado de nivelación del Tamer {client.Tamer.Name}: LevelingStatusId={client.Tamer.LevelingStatus?.Id}", "hatch");
                await GameLogger.LogInfo($"Estado de nivelación del Digimon para Tamer {newDigimon.Character.Name}: LevelingStatusId={newDigimon.Character.LevelingStatus?.Id}", "hatch");

                // Log completion
                await GameLogger.LogInfo($"Fin del procesamiento del paquete HatchFinish para TamerId={client.TamerId}", "hatch");
            }
            catch (Exception ex)
            {
                await GameLogger.LogError($"Error durante procesamiento de HatchFinish para TamerId={client.TamerId}: {ex.Message}, StackTrace={ex.StackTrace}", "hatch");
                client.Send(new SystemMessagePacket("An error occurred while processing the hatch."));
                return;
            }
        }
    }
}