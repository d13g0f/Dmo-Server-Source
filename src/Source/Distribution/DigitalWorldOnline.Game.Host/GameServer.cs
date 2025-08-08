using System.Diagnostics;
using System.Text.Json;
using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.AuthenticationServer;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using GameServer.Logging;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DigitalWorldOnline.Game
{
    public sealed class GameServer : Commons.Entities.GameServer, IHostedService
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfiguration _configuration;
        private readonly IProcessor _processor;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly PvpServer _pvpServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly EventServer _eventServer;
        private readonly PartyManager _partyManager;

        private const int OnConnectEventHandshakeHandler = 65535;

        public GameServer(
            IHostApplicationLifetime hostApplicationLifetime,
            IConfiguration configuration,
            IProcessor processor,
            ILogger logger,
            IMapper mapper,
            ISender sender,
            AssetsLoader assets,
            MapServer mapServer,
            PvpServer pvpServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PartyManager partyManager)
        {
            OnConnect += OnConnectEvent;
            OnDisconnect += OnDisconnectEvent;
            DataReceived += OnDataReceivedEvent;

            _hostApplicationLifetime = hostApplicationLifetime;
            _configuration = configuration;
            _processor = processor;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
            _assets = assets;
            _mapServer = mapServer;
            _pvpServer = pvpServer;
            _dungeonsServer = dungeonsServer;
            _eventServer = eventServer;
            _partyManager = partyManager;
        }

        /// <summary>
        /// Event triggered everytime that a game client connects to the server.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who connected</param>
        private void OnConnectEvent(object sender, GameClientEvent gameClientEvent)
        {
            var clientIpAddress = gameClientEvent.Client.ClientAddress.Split(':')?.FirstOrDefault();

            /*if (InvalidConnection(clientIpAddress))
            {
                _logger.Warning($"Blocked connection event from {gameClientEvent.Client.HiddenAddress}.");

                if (!string.IsNullOrEmpty(clientIpAddress) && !RefusedAddresses.Contains(clientIpAddress))
                    RefusedAddresses.Add(clientIpAddress);

                gameClientEvent.Client.Disconnect();
                RemoveClient(gameClientEvent.Client);
            }*/

            _logger.Information($"Accepted connection event from {gameClientEvent.Client.HiddenAddress}.");

            gameClientEvent.Client.SetHandshake((short)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() &
                                                        OnConnectEventHandshakeHandler));

            if (gameClientEvent.Client.IsConnected)
            {
                _logger.Debug($"Sending handshake for request source {gameClientEvent.Client.ClientAddress}.");
                gameClientEvent.Client.Send(new OnConnectEventConnectionPacket(gameClientEvent.Client.Handshake));
            }
            else
                _logger.Warning($"Request source {gameClientEvent.Client.ClientAddress} has been disconnected.");
        }

       /// <summary>
        /// Event triggered every time the game client disconnects from the server.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who disconnected</param>
        private async void OnDisconnectEvent(object sender, GameClientEvent gameClientEvent)
        {
            var client = gameClientEvent?.Client;
            if (client == null)
            {
                _logger.Error("[Disconnect] gameClientEvent.Client es null.");
                return;
            }

            if (client.TamerId <= 0)
            {
                _logger.Warning($"[Disconnect] Cliente sin TamerId válido. IP={client.ClientAddress} AccountId={client.AccountId}");
                return;
            }

            _logger.Information($"[Disconnect] Recibido evento de desconexión: Tamer={client.Tamer?.Name ?? "Unknown"} TamerId={client.TamerId} IP={client.HiddenAddress} AccountId={client.AccountId}");

            _logger.Debug($"[Disconnect] Detalle conexión: RemoteEndPoint={client.ClientAddress} Handshake={client.Handshake} ServerId={client.ServerId}");

            // Remover cliente del servidor correspondiente
            if (client.DungeonMap)
            {
                _logger.Information($"[Disconnect] Cliente estaba en DungeonMap. Removiendo del servidor de Dungeon. MapId={client.Tamer?.Location.MapId ?? 0} Channel={client.Tamer?.Channel ?? 0}");
                _dungeonsServer?.RemoveClient(client);
            }
            else if (client.EventMap)
            {
                _logger.Information($"[Disconnect] Cliente estaba en EventMap. Removiendo del servidor de Event. MapId={client.Tamer?.Location.MapId ?? 0} Channel={client.Tamer?.Channel ?? 0}");
                _eventServer?.RemoveClient(client);
            }
            else if (client.PvpMap)
            {
                _logger.Information($"[Disconnect] Cliente estaba en PvpMap. Removiendo del servidor de PvP. MapId={client.Tamer?.Location.MapId ?? 0} Channel={client.Tamer?.Channel ?? 0}");
                _pvpServer?.RemoveClient(client);
            }
            else
            {
                _logger.Information($"[Disconnect] Cliente estaba en Map normal. Removiendo del servidor de Map. MapId={client.Tamer?.Location.MapId ?? 0} Channel={client.Tamer?.Channel ?? 0}");
                _mapServer?.RemoveClient(client);
            }

            if (client.GameQuit)
            {
                // Validar Tamer antes de actualizar estado
                if (client.Tamer != null)
                {
                    _logger.Information($"[Disconnect] Flag GameQuit=true. Actualizando estado a Disconnected para Tamer={client.Tamer.Name} TamerId={client.TamerId}");
                    try
                    {
                        client.Tamer.UpdateState(CharacterStateEnum.Disconnected);
                        await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Disconnected));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Disconnect] Error al actualizar estado para TamerId={client.TamerId}: {ex.Message}");
                    }

                    // Ejecutar notificaciones solo si Tamer está inicializado
                    _logger.Information($"[Disconnect] Notificando Friends, Guild, Party, Trader para TamerId={client.TamerId}");
                    try
                    {
                        CharacterFriendsNotification(gameClientEvent);
                        CharacterGuildNotification(gameClientEvent);
                        await PartyNotification(gameClientEvent);
                        CharacterTargetTraderNotification(gameClientEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Disconnect] Error al procesar notificaciones para TamerId={client.TamerId}: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Warning($"[Disconnect] Tamer es null para TamerId={client.TamerId}. Omitiendo actualización de estado y notificaciones.");
                }

                // Ejecutar DungeonWarpGate si corresponde
                if (client.DungeonMap)
                {
                    _logger.Information($"[Disconnect] Cliente estaba en DungeonMap. Ejecutando DungeonWarpGate para TamerId={client.TamerId}");
                    try
                    {
                        await DungeonWarpGate(gameClientEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Disconnect] Error en DungeonWarpGate para TamerId={client.TamerId}: {ex.Message}");
                    }
                }
            }
            else
            {
                _logger.Information($"[Disconnect] GameQuit=false. No se actualiza estado ni se notifican redes sociales para TamerId={client.TamerId}");
            }

            // Limpiar estado del cliente
            _logger.Information($"[Disconnect] Limpiando estado interno del cliente TamerId={client.TamerId} AccountId={client.AccountId}");
            try
            {
                client.ResetState();
                client.SetGameQuit(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Disconnect] Error al limpiar estado para TamerId={client.TamerId}: {ex.Message}");
            }

            await GameLogger.LogInfo(
                $"[Disconnect] Cleanup completo: AccountId={client.AccountId}, TamerId={client.TamerId}, IP={client.ClientAddress}",
                "session/disconnects");
        }

        private async Task PartyNotification(GameClientEvent gameClientEvent)
        {
            var party = _partyManager.FindParty(gameClientEvent.Client.TamerId);

            if (party != null)
            {
                var member = party.Members.FirstOrDefault(x => x.Value.Id == gameClientEvent.Client.TamerId);

                foreach (var target in party.Members.Values)
                {
                    var targetClient = _mapServer.FindClientByTamerId(target.Id);

                    if (targetClient == null) targetClient = _dungeonsServer.FindClientByTamerId(target.Id);

                    if (targetClient == null) continue;

                    targetClient.Send(new PartyMemberDisconnectedPacket(party[gameClientEvent.Client.TamerId].Key)
                        .Serialize());
                }

                if (member.Key == party.LeaderId && party.Members.Count >= 3)
                {
                    party.RemoveMember(party[gameClientEvent.Client.TamerId].Key);

                    var randomIndex = new Random().Next(party.Members.Count);
                    var sortedPlayer = party.Members.ElementAt(randomIndex).Key;

                    foreach (var target in party.Members.Values)
                    {
                        var targetClient = _mapServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) targetClient = _dungeonsServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) continue;

                        targetClient.Send(new PartyLeaderChangedPacket(sortedPlayer).Serialize());
                    }
                }
                else
                {
                    if (party.Members.Count == 2)
                    {
                        var map = UtilitiesFunctions.MapGroup(gameClientEvent.Client.Tamer.Location.MapId);

                        var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(map));
                        var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(map));

                        if (mapConfig == null || waypoints == null || !waypoints.Regions.Any())
                        {
                            gameClientEvent.Client.Send(new SystemMessagePacket($"Map information not found for map Id {map}."));
                            _logger.Warning($"Map information not found for map Id {map} on character {gameClientEvent.Client.TamerId}.");
                            _partyManager.RemoveParty(party.Id);
                            return;
                        }

                        var destination = waypoints.Regions.First();

                        foreach (var pmember in party.Members.Values.Where(x => x.Id != gameClientEvent.Client.Tamer.Id).ToList())
                        {
                            var dungeonClient = _dungeonsServer.FindClientByTamerId(pmember.Id);

                            if (dungeonClient == null) continue;

                            if (dungeonClient.DungeonMap)
                            {
                                _dungeonsServer.RemoveClient(dungeonClient);

                                dungeonClient.Tamer.NewLocation(map, destination.X, destination.Y);
                                await _sender.Send(new UpdateCharacterLocationCommand(dungeonClient.Tamer.Location));

                                dungeonClient.Tamer.Partner.NewLocation(map, destination.X, destination.Y);
                                await _sender.Send(
                                    new UpdateDigimonLocationCommand(dungeonClient.Tamer.Partner.Location));

                                dungeonClient.Tamer.UpdateState(CharacterStateEnum.Loading);
                                await _sender.Send(new UpdateCharacterStateCommand(dungeonClient.TamerId,
                                    CharacterStateEnum.Loading));

                                foreach (var memberId in party.GetMembersIdList())
                                {
                                    var targetDungeon = _dungeonsServer.FindClientByTamerId(memberId);

                                    if (targetDungeon != null)
                                        targetDungeon.Send(new PartyMemberWarpGatePacket(party[dungeonClient.TamerId],
                                                gameClientEvent.Client.Tamer).Serialize());
                                }

                                dungeonClient?.SetGameQuit(false);

                                dungeonClient?.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                                    dungeonClient.Tamer.Location.MapId, dungeonClient.Tamer.Location.X,
                                    dungeonClient.Tamer.Location.Y));
                            }
                        }
                    }

                    party.RemoveMember(party[gameClientEvent.Client.TamerId].Key);
                }

                if (party.Members.Count <= 1)
                    _partyManager.RemoveParty(party.Id);
            }
        }

       private void CharacterGuildNotification(GameClientEvent gameClientEvent)
        {
            // Validar parámetros de entrada
            if (gameClientEvent?.Client == null)
            {
                _logger.Error("CharacterGuildNotification: gameClientEvent o Client es null.");
                return;
            }
            if (gameClientEvent.Client.Tamer == null)
            {
                _logger.Warning($"CharacterGuildNotification: Tamer es null para ClientId={gameClientEvent.Client.AccountId}. Omitiendo notificaciones.");
                return;
            }

            try
            {
                if (gameClientEvent.Client.Tamer.Guild != null) // Línea 292
                {
                    if (gameClientEvent.Client.Tamer.Guild.Members == null)
                    {
                        _logger.Warning($"CharacterGuildNotification: Guild.Members es null para TamerId={gameClientEvent.Client.TamerId}.");
                        return;
                    }

                    foreach (var guildMember in gameClientEvent.Client.Tamer.Guild.Members)
                    {
                        if (guildMember?.CharacterInfo == null)
                        {
                            var guildMemberClient = _mapServer?.FindClientByTamerId(guildMember.CharacterId);
                            if (guildMemberClient != null)
                            {
                                guildMember.SetCharacterInfo(guildMemberClient.Tamer);
                            }
                            else
                            {
                                try
                                {
                                    var characterResult = _sender.Send(new CharacterByIdQuery(guildMember.CharacterId)).Result;
                                    guildMember.SetCharacterInfo(_mapper.Map<CharacterModel>(characterResult));
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error($"Error al obtener CharacterInfo para CharacterId={guildMember.CharacterId}: {ex.Message}");
                                }
                            }
                        }
                    }

                    foreach (var guildMember in gameClientEvent.Client.Tamer.Guild.Members)
                    {
                        if (guildMember == null)
                        {
                            _logger.Warning($"CharacterGuildNotification: guildMember null para TamerId={gameClientEvent.Client.TamerId}.");
                            continue;
                        }

                        _logger.Debug($"Sending guild member disconnection packet for character {guildMember.CharacterId}...");
                        _logger.Debug($"Sending guild information packet for character {gameClientEvent.Client.TamerId}...");

                        var disconnectPacket = new GuildMemberDisconnectPacket(gameClientEvent.Client.Tamer.Name ?? "Unknown").Serialize();
                        var infoPacket = new GuildInformationPacket(gameClientEvent.Client.Tamer.Guild).Serialize();

                        _mapServer?.BroadcastForUniqueTamer(guildMember.CharacterId, disconnectPacket);
                        _mapServer?.BroadcastForUniqueTamer(guildMember.CharacterId, infoPacket);
                        _dungeonsServer?.BroadcastForUniqueTamer(guildMember.CharacterId, disconnectPacket);
                        _dungeonsServer?.BroadcastForUniqueTamer(guildMember.CharacterId, infoPacket);
                        _eventServer?.BroadcastForUniqueTamer(guildMember.CharacterId, disconnectPacket);
                        _eventServer?.BroadcastForUniqueTamer(guildMember.CharacterId, infoPacket);
                        _pvpServer?.BroadcastForUniqueTamer(guildMember.CharacterId, disconnectPacket);
                        _pvpServer?.BroadcastForUniqueTamer(guildMember.CharacterId, infoPacket);
                    }
                }
                else
                {
                    _logger.Debug($"CharacterGuildNotification: TamerId={gameClientEvent.Client.TamerId} no pertenece a un gremio. Omitiendo notificaciones.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error en CharacterGuildNotification para TamerId={gameClientEvent.Client.TamerId}: {ex.Message}");
            }
        }

      private async void CharacterFriendsNotification(GameClientEvent gameClientEvent)
    {
        // Validar parámetros de entrada
        if (gameClientEvent?.Client == null)
        {
            _logger.Error("CharacterFriendsNotification: gameClientEvent o Client es null.");
            return;
        }
        if (gameClientEvent.Client.Tamer == null)
        {
            _logger.Warning($"CharacterFriendsNotification: Tamer es null para ClientId={gameClientEvent.Client.AccountId}. Omitiendo notificaciones.");
            return;
        }
        if (gameClientEvent.Client.Tamer.Friended == null)
        {
            _logger.Warning($"CharacterFriendsNotification: Friended es null para TamerId={gameClientEvent.Client.TamerId}. Omitiendo notificaciones.");
            return;
        }

        try
        {
            // Iterar sobre la lista de amigos
            gameClientEvent.Client.Tamer.Friended.ForEach(friend =>
            {
                if (friend == null)
                {
                    _logger.Warning($"CharacterFriendsNotification: Amigo null encontrado para TamerId={gameClientEvent.Client.TamerId}.");
                    return;
                }

                _logger.Debug($"Sending friend disconnection packet for character {friend.FriendId}...");

                // Crear el paquete una vez para reutilizarlo (DRY)
                var packet = new FriendDisconnectPacket(gameClientEvent.Client.Tamer.Name ?? "Unknown").Serialize();

                // Enviar a los servidores, verificando que no sean null
                _mapServer?.BroadcastForUniqueTamer(friend.FriendId, packet);
                _dungeonsServer?.BroadcastForUniqueTamer(friend.FriendId, packet);
                _eventServer?.BroadcastForUniqueTamer(friend.FriendId, packet);
                _pvpServer?.BroadcastForUniqueTamer(friend.FriendId, packet);
            });

            // Actualizar el estado de los amigos
            await _sender.Send(new UpdateCharacterFriendsCommand(gameClientEvent.Client.Tamer, false));
        }
        catch (Exception ex)
        {
            _logger.Error($"Error en CharacterFriendsNotification para TamerId={gameClientEvent.Client.TamerId}: {ex.Message}");
        }
    }

        private void CharacterTargetTraderNotification(GameClientEvent gameClientEvent)
        {
            if (gameClientEvent.Client.Tamer.TargetTradeGeneralHandle != 0)
            {
                if (gameClientEvent.Client.DungeonMap)
                {
                    var targetClient =
                        _dungeonsServer.FindClientByTamerHandle(gameClientEvent.Client.Tamer.TargetTradeGeneralHandle);

                    if (targetClient != null)
                    {
                        targetClient.Send(new TradeCancelPacket(gameClientEvent.Client.Tamer.GeneralHandler));
                        targetClient.Tamer.ClearTrade();
                    }
                }
                else
                {
                    var targetClient = _mapServer.FindClientByTamerHandleAndChannel(
                        gameClientEvent.Client.Tamer.TargetTradeGeneralHandle, gameClientEvent.Client.TamerId);

                    if (targetClient != null)
                    {
                        targetClient.Send(new TradeCancelPacket(gameClientEvent.Client.Tamer.GeneralHandler));
                        targetClient.Tamer.ClearTrade();
                    }
                }
            }
        }

        private async Task DungeonWarpGate(GameClientEvent gameClientEvent)
        {
            if (gameClientEvent.Client.DungeonMap)
            {
                var map = UtilitiesFunctions.MapGroup(gameClientEvent.Client.Tamer.Location.MapId);

                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(map));
                var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(map));

                if (mapConfig == null || waypoints == null || !waypoints.Regions.Any())
                {
                    gameClientEvent.Client.Send(
                        new SystemMessagePacket($"Map information not found for map Id {map}."));
                    _logger.Warning(
                        $"Map information not found for map Id {map} on character {gameClientEvent.Client.TamerId} Dungeon Portal");
                    return;
                }

                var destination = waypoints.Regions.First();

                gameClientEvent.Client.Tamer.NewLocation(map, destination.X, destination.Y);
                await _sender.Send(new UpdateCharacterLocationCommand(gameClientEvent.Client.Tamer.Location));

                gameClientEvent.Client.Tamer.Partner.NewLocation(map, destination.X, destination.Y);
                await _sender.Send(new UpdateDigimonLocationCommand(gameClientEvent.Client.Tamer.Partner.Location));

                gameClientEvent.Client.Tamer.UpdateState(CharacterStateEnum.Loading);
                await _sender.Send(new UpdateCharacterStateCommand(gameClientEvent.Client.TamerId,
                    CharacterStateEnum.Loading));
            }
        }

        /// <summary>
        /// Event triggered everytime the game client sends a TCP packet.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who sent the packet</param>
        /// <param name="data">The packet content, in byte array</param>
        private void OnDataReceivedEvent(object sender, GameClientEvent gameClientEvent, byte[] data)
        {
            try
            {
                _logger.Debug($"Received {data.Length} bytes from {gameClientEvent.Client.ClientAddress}.");
                _processor.ProcessPacketAsync(gameClientEvent.Client, data);
            }
            catch (NotImplementedException)
            {
                gameClientEvent.Client.Send(new SystemMessagePacket($"Feature under development."));
            }
            catch (Exception ex)
            {
                gameClientEvent.Client.SetGameQuit(true);
                gameClientEvent.Client.Disconnect();

                _logger.Error($"Process packet error: {ex.Message} {ex.InnerException} {ex.StackTrace}.");

                try
                {
                    var filePath = $"PacketErrors/{gameClientEvent.Client.ClientAddress}_{DateTime.Now}.txt";

                    using var fs = File.Create(filePath);
                    fs.Write(data, 0, data.Length);
                }
                catch
                {
                }

                //TODO: Salvar no banco com os parametros
            }
        }

        /// <summary>
        /// The default hosted service "starting" method.
        /// </summary>
        /// <param name="cancellationToken">Control token for the operation</param>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Information($"Starting {GetType().Name}...");

            Console.Title = $"{GetType().Name} 1.0";

            _hostApplicationLifetime.ApplicationStarted.Register(OnStarted);
            _hostApplicationLifetime.ApplicationStopping.Register(OnStopping);
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);

            // Verify new evolutions on existent digimon
            Task.Run(CheckAllDigimonEvolutions);
            // Verify encyclopedia
            Task.Run(CheckEncyclopedia);

            Task.Run(() => _mapServer.StartAsync(cancellationToken));
            Task.Run(() => _mapServer.LoadAllMaps(cancellationToken));
            Task.Run(() => _dungeonsServer.StartAsync(cancellationToken));
            //Task.Run(() => _pvpServer.StartAsync(cancellationToken));
            //Task.Run(() => _eventServer.StartAsync(cancellationToken));

            Task.Run(() => _sender.Send(new UpdateCharacterFriendsCommand(null, false)));

            return Task.CompletedTask;
        }

        /// <summary>
        /// The default hosted service "stopping" method
        /// </summary>
        /// <param name="cancellationToken">Control token for the operation</param>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// The default hosted service "started" method action
        /// </summary>
        private void OnStarted()
        {
            if (!Listen(_configuration[GameServerAddress], _configuration[GameServerPort], _configuration[GameServerBacklog]))
            {
                _logger.Error("Unable to start. Check the binding configurations.");
                _hostApplicationLifetime.StopApplication();
                return;
            }

            _logger.Information($"{GetType().Name} started.");

            _sender.Send(new UpdateCharactersStateCommand(CharacterStateEnum.Disconnected));
        }

        /// <summary>
        /// The default hosted service "stopping" method action
        /// </summary>
        private void OnStopping()
        {
            try
            {
                _logger.Information($"Disconnecting clients from {GetType().Name}...");

                Task.Run(async () => await _sender.Send(new UpdateCharacterFriendsCommand(null, false)));

                Shutdown();
                return;
            }
            catch (Exception e)
            {
                throw; // TODO handle exception
            }
        }

        /// <summary>
        /// The default hosted service "stopped" method action
        /// </summary>
        private void OnStopped()
        {
            _logger.Information($"{GetType().Name} stopped.");
        }

           
       private async Task CheckAllDigimonEvolutions()
{
    try
    {
        var swGlobal = Stopwatch.StartNew();
        int skip = 0;
        int take = 200;

        var evolutionAssetCache = new Dictionary<int, EvolutionAssetModel>();

        while (true)
        {
            var swBatch = Stopwatch.StartNew();
            var digimons = _mapper.Map<List<DigimonModel>>(await _sender.Send(new GetAllCharactersDigimonQuery(skip, take)));
            swBatch.Stop();

            if (digimons.Count == 0)
                break;

            _logger.Information($"[CheckAllDigimonEvolutions] :: Batch {skip}-{skip + take} fetched {digimons.Count} digimons in {swBatch.ElapsedMilliseconds} ms");

            foreach (var digimon in digimons)
            {

                if (!evolutionAssetCache.TryGetValue(digimon.BaseType, out var evolutionAsset))
                {
                    var swInner = Stopwatch.StartNew();
                    evolutionAsset = _mapper.Map<EvolutionAssetModel>(
                        await _sender.Send(new DigimonEvolutionAssetsByTypeQuery(digimon.BaseType)));
                    swInner.Stop();

                    evolutionAssetCache[digimon.BaseType] = evolutionAsset;

                }

                if (evolutionAsset == null)
                {
                    _logger.Warning($"[CheckAllDigimonEvolutions] :: EvolutionAsset is NULL for BaseType: {digimon.BaseType}");
                    continue;
                }

                foreach (var line in evolutionAsset.Lines)
                {
                    if (!digimon.Evolutions.Exists(x => x.Type == line.Type))
                    {
                        _logger.Information($"[CheckAllDigimonEvolutions] :: Digimon BaseType: {digimon.BaseType} missing EvolutionLine: {line.Type}");
                    }
                }

                foreach (var evolution in digimon.Evolutions)
                {
                    try
                    {
                        var evolutionLine = evolutionAsset.Lines.FirstOrDefault(y => y.Type == evolution.Type);
                        if (evolutionLine == null)
                        {
                            _logger.Warning($"[CheckAllDigimonEvolutions] :: EvolutionLine not found for EvolutionType: {evolution.Type} on BaseType: {digimon.BaseType}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[CheckAllDigimonEvolutions][InnerLoop] :: {ex.Message}");
                        _logger.Error(ex.StackTrace);
                    }
                }
            }

            skip += take;
        }

        swGlobal.Stop();
        _logger.Information($"[CheckAllDigimonEvolutions] :: Completed in {swGlobal.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        _logger.Error($"[CheckAllDigimonEvolutions][Outer] :: {ex.Message}");
        if (ex.InnerException != null)
            _logger.Error($"[CheckAllDigimonEvolutions][InnerException] :: {ex.InnerException}");
    }
}

   private async Task CheckEncyclopedia()
        {
            try
            {
                var swGlobal = Stopwatch.StartNew();
                _logger.Information($"[CheckEncyclopedia] :: Started processing");

                int skip = 0;
                int take = 100;

                var evolutionAssetCache = new Dictionary<int, EvolutionAssetModel>();

                int encyclopediaCreated = 0;
                int evolutionsAdded = 0;

                while (true)
                {
                    var digimons = _mapper.Map<List<DigimonModel>>(await _sender.Send(new GetAllCharactersDigimonQuery(skip, take)));

                    if (digimons.Count == 0)
                        break;

                    foreach (var digimon in digimons)
                    {
                        if (!evolutionAssetCache.TryGetValue(digimon.BaseType, out var evolutionAsset))
                        {
                            evolutionAsset = _mapper.Map<EvolutionAssetModel>(
                                await _sender.Send(new DigimonEvolutionAssetsByTypeQuery(digimon.BaseType)));
                            evolutionAssetCache[digimon.BaseType] = evolutionAsset;
                        }

                        if (evolutionAsset == null)
                        {
                            _logger.Error($"[CheckEncyclopedia] :: EvolutionAsset is NULL for BaseType: {digimon.BaseType}");
                            continue;
                        }

                        bool encyclopediaExists = digimon.Character.Encyclopedia?.Any(x => x.DigimonEvolutionId == evolutionAsset.Id) ?? false;

                        if (!encyclopediaExists)
                        {
                            var encyclopedia = CharacterEncyclopediaModel.Create(
                                digimon.Character.Id,
                                evolutionAsset.Id,
                                digimon.Level,
                                digimon.Size,
                                digimon.Digiclone.ATLevel,
                                digimon.Digiclone.BLLevel,
                                digimon.Digiclone.CTLevel,
                                digimon.Digiclone.EVLevel,
                                digimon.Digiclone.HPLevel,
                                digimon.Evolutions.All(x => Convert.ToBoolean(x.Unlocked)),
                                false
                            );

                            foreach (var evo in digimon.Evolutions)
                            {
                                var evolutionLine = evolutionAsset.Lines.FirstOrDefault(y => y.Type == evo.Type);
                                byte slotLevel = evolutionLine?.SlotLevel ?? 0;

                                var encyclopediaEvo = CharacterEncyclopediaEvolutionsModel.Create(evo.Type, slotLevel, Convert.ToBoolean(evo.Unlocked));
                                encyclopedia.Evolutions.Add(encyclopediaEvo);
                            }

                            var created = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));
                            digimon.Character.Encyclopedia.Add(created);

                            encyclopediaCreated++;
                        }
                        else
                        {
                            foreach (var evo in digimon.Evolutions)
                            {
                                try
                                {
                                    var evolutionLine = evolutionAsset.Lines.FirstOrDefault(y => y.Type == evo.Type);
                                    byte slotLevel = evolutionLine?.SlotLevel ?? 0;

                                    var encyclopediaEntry = digimon.Character.Encyclopedia.First(x => x.DigimonEvolutionId == evolutionAsset.Id);

                                    if (!encyclopediaEntry.Evolutions.Any(e => e.DigimonBaseType == evo.Type))
                                    {
                                        var encyclopediaEvo = CharacterEncyclopediaEvolutionsModel.Create(evo.Type, slotLevel, Convert.ToBoolean(evo.Unlocked));
                                        encyclopediaEntry.Evolutions.Add(encyclopediaEvo);
                                        evolutionsAdded++;

                                        if (encyclopediaEntry.Evolutions.All(e => e.IsUnlocked))
                                        {
                                            if (!encyclopediaEntry.IsRewardReceived)
                                            {
                                                encyclopediaEntry.SetRewardAllowed(true);
                                                encyclopediaEntry.SetRewardReceived(false);

                                                if (encyclopediaEntry.Evolutions == null)
                                                {
                                                    _logger.Error($"[CheckEncyclopedia] :: Skipping update for DigimonEvolutionId: {encyclopediaEntry.DigimonEvolutionId} due to null Evolutions list");
                                                    continue;
                                                }

                                                await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopediaEntry));
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error($"[CheckEncyclopedia][InnerLoop] :: Failed to process evolution for DigimonBaseType: {digimon.BaseType}, EvolutionType: {evo.Type}. Error: {ex.Message}");
                                    _logger.Error(ex.StackTrace);
                                }
                            }
                        }
                    }

                    skip += take;
                }

                swGlobal.Stop();
                _logger.Information($"[CheckEncyclopedia] :: Finished in {swGlobal.ElapsedMilliseconds} ms, Created {encyclopediaCreated} new Encyclopedias, Added {evolutionsAdded} evolutions.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[CheckEncyclopedia][Outer] :: {ex.Message}");
                if (ex.InnerException != null)
                    _logger.Error($"[CheckEncyclopedia][InnerException] :: {ex.InnerException}");
            }
        }



    }
}


