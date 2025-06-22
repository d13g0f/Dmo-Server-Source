using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Writers;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Text.RegularExpressions;
using DigitalWorldOnline.Game.PacketProcessors;
using System.Threading.Channels;
using System.Drawing;
using DigitalWorldOnline.Commons.Models.Servers;
using DigitalWorldOnline.Commons.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using System.Net.NetworkInformation;

namespace DigitalWorldOnline.Game
{
    public sealed class GameMasterCommandsProcessor : IDisposable
    {
        private const string GameServerAddress = "GameServer:Address";
        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";

        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly ExpManager _expManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;

        private Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)> commands;

        public GameMasterCommandsProcessor(PartyManager partyManager, StatusManager statusManager, ExpManager expManager, AssetsLoader assets,
            MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer,
            ILogger logger, ISender sender, IMapper mapper, IConfiguration configuration)
        {
            _partyManager = partyManager;
            _expManager = expManager;
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
            _configuration = configuration;
            InitializeCommands();
        }

        // Comands and permissions
        private void InitializeCommands()
        {
            commands = new Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)>
            {
                { "clear", (ClearCommand, null) },
                { "battlelog", (BattleLogCommand, null) },
                { "players", (PlayersCommand, null) },
                { "stats", (StatsCommand, null) },
                { "time", (TimeCommand, null) },
                { "item", (ItemCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "itemto", (ItemToCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "bits", (BitsCommand, null) },
                { "bitsto", (CrownToCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "crown", (CrownCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "crownto", (CrownToCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "silk", (SilkCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "ban", (BanCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "pvp", (PvpCommand, null) },
                { "hatch", (HatchCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "godmode", (GodmodeCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "burnexp", (BurnexpCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "membership", (MembershipCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "cashitem", (CashItemCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "cashitemto", (CashItemToCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "reload", (ReloadCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Administrator }) },
                { "help", (HelpCommand, null) },
                { "ping", (PingCommand, null) },
            };
        }

        public async Task ExecuteCommand(GameClient client, string message)
        {
            var command = Regex.Replace(message.Trim(), @"\s+", " ").Split(' ');

            if (commands.TryGetValue(command[0], out var commandInfo))
            {
                if (commandInfo.AccessLevels?.Contains(client.AccessLevel) != false)
                {
                    await commandInfo.Command(client, command);

                    _logger.Information($"GM AccountID: {client.AccountId} Tamer: {client.Tamer.Name} used Command !{message}");
                }
                else
                {
                    _logger.Warning($"Tamer {client.Tamer.Name} tryed to use the command {message} without permission !! [GMCommand]");
                    client.Send(new SystemMessagePacket("You do not have permission to use this command."));
                }
            }
            else
            {
                //client.Send(new SystemMessagePacket($"Invalid Command !! Type !help"));
                await ExecuteCommand2(client, message);
            }
        }

        public async Task ExecuteCommand2(GameClient client, string message)
        {
            var command = Regex.Replace(message.Trim(), @"\s+", " ").Split(' ');

            if (message.Contains("summon") && command[0].ToLower() != "summon")
            {
                command[2] = message.Split(' ')[2];
            }

            _logger.Information($"GM AccountID: {client.AccountId} Tamer: {client.Tamer.Name} used Command !{message}");

            switch (command[0])
            {
                case "blessing":
                    {
                        if (client.Tamer.AccountId != 2885 || client.Tamer.AccountId != 1366) break;

                        var mapId = client.Tamer.Location.MapId;
                        var currentMap = _mapServer.Maps.FirstOrDefault(gameMap => gameMap.Clients.Any() && gameMap.MapId == mapId);

                        if (currentMap == null) break; // Prevent null reference errors

                        var clients = currentMap.Clients;
                        var buffData = new List<(int BuffId, int Value1, int Value2)>
                            {
                                (40149, 6699724, 3600),
                            };

                        foreach (var clientAll in clients)
                        {
                            foreach (var (BuffId, Value1, Value2) in buffData)
                            {
                                var buff = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == Value1);
                                var activeBuff = clientAll.Tamer.Partner.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == BuffId);
                                var BuffInfo = clientAll.Tamer.Partner.BuffList.Buffs.FirstOrDefault(x => x.BuffId == BuffId);

                                Action<long, byte[]> broadcastAction = clientAll.DungeonMap
                                    ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                                    : clientAll.PvpMap
                                        ? (id, data) => _pvpServer.BroadcastForTamerViewsAndSelf(id, data)
                                        : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

                                if (activeBuff != null)
                                {
                                    activeBuff.IncreaseDuration(Value2);

                                    var duration = UtilitiesFunctions.RemainingTimeSeconds(activeBuff.Duration);


                                    broadcastAction(clientAll.TamerId,
                                        new UpdateBuffPacket(clientAll.Tamer.Partner.GeneralHandler, buff, (short)0, duration)
                                            .Serialize());

                                    broadcastAction(clientAll.TamerId,
                                        new AddBuffPacket(clientAll.Tamer.Partner.GeneralHandler, buff, (short)0, duration)
                                            .Serialize());
                                }
                                else
                                {
                                    var duration = UtilitiesFunctions.RemainingTimeSeconds(Value2);
                                    var newDigimonBuff = DigimonBuffModel.Create(BuffId, Value1, 0, Value2);

                                    if (!clientAll.Tamer.Partner.BuffList.Buffs.Any(x => x.BuffId == BuffId))
                                    {
                                        clientAll.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                    }

                                    newDigimonBuff.SetBuffInfo(buff);

                                    broadcastAction(clientAll.TamerId,
                                        new AddBuffPacket(clientAll.Tamer.Partner.GeneralHandler, buff, (short)0, duration)
                                            .Serialize());
                                }
                            }

                            await _sender.Send(new UpdateDigimonBuffListCommand(clientAll.Tamer.Partner.BuffList));
                            clientAll.Send(new UpdateStatusPacket(clientAll.Tamer));
                            clientAll.Send(new NoticeMessagePacket("The Area has been buffed by Owner"));
                        }
                    }
                    break;

                case "delete":
                    {
                        var regex = @"^delete";
                        var match = Regex.IsMatch(message, regex, RegexOptions.IgnoreCase);

                        if (!match)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !delete (slot) (email)"));
                            break;
                        }

                        if (command.Length < 3)
                        {
                            client.Send(new SystemMessagePacket("Invalid command.\nType !delete (slot) (email)"));
                            break;
                        }

                        if (!byte.TryParse(command[1], out byte digiSlot))
                        {
                            client.Send(new SystemMessagePacket("Invalid Slot.\nType a valid Slot (1 to 4)"));
                            break;
                        }

                        if (digiSlot == 0)
                        {
                            client.Send(new SystemMessagePacket($"Digimon in slot 0 cant be deleted !!"));
                            break;
                        }

                        string validation = command[2];

                        var digimon = client.Tamer.Digimons.FirstOrDefault(x => x.Slot == digiSlot);

                        if (digimon == null)
                        {
                            client.Send(new SystemMessagePacket($"Digimon not found on slot {digiSlot}"));
                            break;
                        }

                        var digimonId = digimon.Id;

                        var result = client.PartnerDeleteValidation(validation);

                        if (result > 0)
                        {
                            client.Tamer.RemoveDigimon(digiSlot);

                            client.Send(new PartnerDeletePacket(digiSlot));

                            await _sender.Send(new DeleteDigimonCommand(digimonId));

                            _logger.Verbose($"Tamer {client.Tamer.Name} deleted partner {digimonId}.");
                        }
                        else
                        {
                            client.Send(new PartnerDeletePacket(result));
                            _logger.Verbose(
                                $"Tamer {client.Tamer.Name} failed to deleted partner {digimonId} with invalid account information.");
                        }
                    }
                    break;

                /*case "done":
                    {
                        var regex = @"^done";
                        var match = Regex.IsMatch(message, regex, RegexOptions.IgnoreCase);

                        if (!match)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !done (slot)"));
                            break;
                        }

                        if (command.Length < 2)
                        {
                            client.Send(new SystemMessagePacket("Invalid command.\nType !delete (slot)"));
                            break;
                        }

                        if (!byte.TryParse(command[1], out byte digiSlot))
                        {
                            client.Send(new SystemMessagePacket("Invalid Slot.\nType a valid Slot (1 to 4)"));
                            break;
                        }

                        if (digiSlot == 0 || digiSlot > 4)
                        {
                            client.Send(new SystemMessagePacket($"Invalid Slot !!"));
                            break;
                        }

                        var digimon = client.Tamer.Digimons.FirstOrDefault(x => x.Slot == digiSlot);

                        if (digimon == null)
                        {
                            client.Send(new SystemMessagePacket($"Digimon not found on slot {digiSlot}"));
                            break;
                        }
                        else
                        {
                            var digimonId = digimon.Id;

                            if (digimon.BaseType == 31066 && digimon.Level >= 99)
                            {
                                var itemId = 66935; // 66935 impmon item

                                var newItem = new ItemModel();
                                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                                if (newItem.ItemInfo == null)
                                {
                                    _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                                    client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                                    break;
                                }

                                newItem.ItemId = itemId;
                                newItem.Amount = 1;

                                if (newItem.IsTemporary)
                                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                                var itemClone = (ItemModel)newItem.Clone();

                                if (client.Tamer.Inventory.AddItem(newItem))
                                {
                                    client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                                }
                                else
                                {
                                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                                    client.Send(new SystemMessagePacket($"Inventory full !!"));
                                    break;
                                }

                                client.Tamer.RemoveDigimon(digiSlot);

                                client.Send(new PartnerDeletePacket(digiSlot));

                                await _sender.Send(new DeleteDigimonCommand(digimonId));
                            }
                            else if (digimon.BaseType == 31023 && digimon.Level >= 99) // Sleipmon Type
                            {
                                var itemId = 66936; // 66936 slepymon item

                                var newItem = new ItemModel();
                                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                                if (newItem.ItemInfo == null)
                                {
                                    _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                                    client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                                    break;
                                }

                                newItem.ItemId = itemId;
                                newItem.Amount = 1;

                                if (newItem.IsTemporary)
                                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                                var itemClone = (ItemModel)newItem.Clone();

                                if (client.Tamer.Inventory.AddItem(newItem))
                                {
                                    client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                                }
                                else
                                {
                                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                                    client.Send(new SystemMessagePacket($"Inventory full !!"));
                                    break;
                                }

                                client.Tamer.RemoveDigimon(digiSlot);

                                client.Send(new PartnerDeletePacket(digiSlot));

                                await _sender.Send(new DeleteDigimonCommand(digimonId));
                            }
                            else if (digimon.BaseType == 31022 && digimon.Level >= 99) // Dynasmon Type
                            {
                                var itemId = 66937; // 66937 dynasmon item

                                var newItem = new ItemModel();
                                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                                if (newItem.ItemInfo == null)
                                {
                                    _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                                    client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                                    break;
                                }

                                newItem.ItemId = itemId;
                                newItem.Amount = 1;

                                if (newItem.IsTemporary)
                                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                                var itemClone = (ItemModel)newItem.Clone();

                                if (client.Tamer.Inventory.AddItem(newItem))
                                {
                                    client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                                }
                                else
                                {
                                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                                    client.Send(new SystemMessagePacket($"Inventory full !!"));
                                    break;
                                }

                                client.Tamer.RemoveDigimon(digiSlot);

                                client.Send(new PartnerDeletePacket(digiSlot));

                                await _sender.Send(new DeleteDigimonCommand(digimonId));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket("Wrong digimon type or level less than 99!!"));
                                break;
                            }
                        }
                    }
                    break;*/

                case "tamer":
                    {
                        if (command.Length == 1)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        switch (command[1])
                        {
                            case "size":
                                {
                                    var regex = @"(tamer\ssize\s\d){1}";
                                    var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                                    if (!match.Success)
                                    {
                                        client.Send(new SystemMessagePacket(
                                            $"Unknown command. Check the available commands on the Admin Portal."));
                                        break;
                                    }

                                    if (short.TryParse(command[2], out var value))
                                    {
                                        client.Tamer.SetSize(value);

                                        if (client.DungeonMap)
                                        {
                                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                new UpdateSizePacket(client.Tamer.GeneralHandler, client.Tamer.Size)
                                                    .Serialize());
                                        }
                                        else
                                        {
                                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                new UpdateSizePacket(client.Tamer.GeneralHandler, client.Tamer.Size)
                                                    .Serialize());
                                        }

                                        await _sender.Send(new UpdateCharacterSizeCommand(client.TamerId, value));
                                    }
                                    else
                                    {
                                        client.Send(
                                            new SystemMessagePacket(
                                                $"Invalid value. Max possible amount is {short.MaxValue}."));
                                    }
                                }
                                break;

                            case "exp":
                                {
                                    //TODO: refazer
                                    var regex = @"(tamer\sexp\sadd\s\d){1}|(tamer\sexp\sremove\s\d){1}|(tamer\sexp\smax){1}";
                                    var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                                    if (!match.Success)
                                    {
                                        client.Send(new SystemMessagePacket("Correct usage is \"!tamer exp add value\" or " +
                                                                            "\"!tamer exp remove value\"" +
                                                                            "\"!tamer exp max\".")
                                            .Serialize());

                                        break;
                                    }

                                    switch (command[2])
                                    {
                                        case "max":
                                            {
                                                if (client.Tamer.Level >= (int)GeneralSizeEnum.TamerLevelMax)
                                                {
                                                    client.Send(new SystemMessagePacket($"Tamer already at max level."));
                                                    break;
                                                }

                                                var result = _expManager.ReceiveMaxTamerExperience(client.Tamer);

                                                if (result.Success)
                                                {
                                                    client.Send(
                                                        new ReceiveExpPacket(
                                                            0,
                                                            0,
                                                            client.Tamer.CurrentExperience,
                                                            client.Tamer.Partner.GeneralHandler,
                                                            0,
                                                            0,
                                                            client.Tamer.Partner.CurrentExperience,
                                                            0
                                                        )
                                                    );
                                                }
                                                else
                                                {
                                                    client.Send(new SystemMessagePacket(
                                                        $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                    return;
                                                }

                                                if (result.LevelGain > 0)
                                                {
                                                    client.Tamer.SetLevelStatus(
                                                        _statusManager.GetTamerLevelStatus(
                                                            client.Tamer.Model,
                                                            client.Tamer.Level
                                                        )
                                                    );

                                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                        new LevelUpPacket(client.Tamer.GeneralHandler, client.Tamer.Level)
                                                            .Serialize());

                                                    client.Tamer.FullHeal();

                                                    client.Send(new UpdateStatusPacket(client.Tamer));

                                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                        new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                                }

                                                if (result.Success)
                                                {
                                                    await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                        client.Tamer.CurrentExperience, client.Tamer.Level));
                                                }
                                            }
                                            break;

                                        case "add":
                                            {
                                                if (client.Tamer.Level >= (int)GeneralSizeEnum.TamerLevelMax)
                                                {
                                                    client.Send(new SystemMessagePacket($"Tamer already at max level."));
                                                    break;
                                                }

                                                var value = Convert.ToInt64(command[3]);

                                                var result = _expManager.ReceiveTamerExperience(value, client.Tamer);

                                                if (result.Success)
                                                {
                                                    client.Send(
                                                        new ReceiveExpPacket(
                                                            value,
                                                            0,
                                                            client.Tamer.CurrentExperience,
                                                            client.Tamer.Partner.GeneralHandler,
                                                            0,
                                                            0,
                                                            client.Tamer.Partner.CurrentExperience,
                                                            0
                                                        )
                                                    );
                                                }
                                                else
                                                {
                                                    client.Send(new SystemMessagePacket(
                                                        $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                    return;
                                                }

                                                if (result.LevelGain > 0)
                                                {
                                                    client.Tamer.SetLevelStatus(
                                                        _statusManager.GetTamerLevelStatus(
                                                            client.Tamer.Model,
                                                            client.Tamer.Level
                                                        )
                                                    );

                                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                                        client.TamerId,
                                                        new LevelUpPacket(
                                                            client.Tamer.GeneralHandler,
                                                            client.Tamer.Level).Serialize());

                                                    client.Tamer.FullHeal();

                                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                                }

                                                if (result.Success)
                                                    await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                        client.Tamer.CurrentExperience, client.Tamer.Level));
                                            }
                                            break;

                                        case "remove":
                                            {
                                                var value = Convert.ToInt64(command[3]);

                                                var tamerInfos = _assets.TamerLevelInfo
                                                    .Where(x => x.Type == client.Tamer.Model)
                                                    .ToList();

                                                if (tamerInfos == null || !tamerInfos.Any() ||
                                                    tamerInfos.Count != (int)GeneralSizeEnum.TamerLevelMax)
                                                {
                                                    _logger.Warning($"Incomplete level config for tamer {client.Tamer.Model}.");

                                                    client.Send(new SystemMessagePacket
                                                        ($"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                    break;
                                                }

                                                //TODO: ajeitar
                                                client.Tamer.LooseExp(value);

                                                client.Send(new ReceiveExpPacket(
                                                    value * -1,
                                                    0,
                                                    client.Tamer.CurrentExperience,
                                                    client.Tamer.Partner.GeneralHandler,
                                                    0,
                                                    0,
                                                    client.Tamer.Partner.CurrentExperience,
                                                    0
                                                ));

                                                await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                    client.Tamer.CurrentExperience, client.Tamer.Level));
                                            }
                                            break;

                                        default:
                                            {
                                                client.Send(new SystemMessagePacket(
                                                    "Correct usage is \"!tamer exp add {value}\" or " +
                                                    "\"!tamer exp max\"."));
                                            }
                                            break;
                                    }
                                }
                                break;

                            case "summon":
                                {
                                    var tamerName = command[2];
                                    var TargetSummon = _mapServer.FindClientByTamerName(tamerName);

                                    if (TargetSummon == null) TargetSummon = _dungeonServer.FindClientByTamerName(tamerName);

                                    if (TargetSummon == null)
                                    {
                                        client.Send(new SystemMessagePacket($"Tamer {tamerName} is not online!"));
                                        return;
                                    }

                                    _logger.Information(
                                        $"Tamer: {client.Tamer.Name} is summoning Tamer: {TargetSummon.Tamer.Name}");
                                    var mapId = client.Tamer.Location.MapId;
                                    var destination = client.Tamer.Location;

                                    if (TargetSummon.DungeonMap)
                                        _dungeonServer.RemoveClient(TargetSummon);
                                    else
                                        _mapServer.RemoveClient(TargetSummon);

                                    TargetSummon.Tamer.NewLocation(mapId, destination.X, destination.Y);
                                    await _sender.Send(new UpdateCharacterLocationCommand(TargetSummon.Tamer.Location));

                                    TargetSummon.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                                    await _sender.Send(new UpdateDigimonLocationCommand(TargetSummon.Tamer.Partner.Location));

                                    TargetSummon.Tamer.UpdateState(CharacterStateEnum.Loading);
                                    await _sender.Send(new UpdateCharacterStateCommand(TargetSummon.TamerId,
                                        CharacterStateEnum.Loading));

                                    TargetSummon.SetGameQuit(false);

                                    TargetSummon.Send(new MapSwapPacket(_configuration[GamerServerPublic],
                                        _configuration[GameServerPort],
                                        client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));

                                    var party = _partyManager.FindParty(TargetSummon.TamerId);

                                    if (party != null)
                                    {
                                        party.UpdateMember(party[TargetSummon.TamerId], TargetSummon.Tamer);

                                        _mapServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                            new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer)
                                                .Serialize());

                                        _dungeonServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                            new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer)
                                                .Serialize());
                                    }

                                    client.Send(new SystemMessagePacket($"You summoned Tamer: {TargetSummon.Tamer.Name}"));
                                    TargetSummon.Send(
                                        new SystemMessagePacket($"You have been summoned by Tamer: {client.Tamer.Name}"));
                                }
                                break;

                            default:
                                {
                                    client.Send(new SystemMessagePacket("Under development."));
                                }
                                break;
                        }
                    }
                    break;

                case "digimon":
                    {
                        if (command.Length == 1)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        switch (command[1])
                        {
                            case "transcend":
                                {
                                    var regex = @"(digimon\stranscend){1}";
                                    var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                                    if (!match.Success)
                                    {
                                        client.Send(new SystemMessagePacket(
                                            $"Unknown command. Check the available commands on the Admin Portal."));
                                        break;
                                    }

                                    client.Partner.Transcend();
                                    client.Partner.SetSize(14000);

                                    client.Partner.SetBaseStatus(
                                        _statusManager.GetDigimonBaseStatus(
                                            client.Partner.CurrentType,
                                            client.Partner.Level,
                                            client.Partner.Size
                                        )
                                    );

                                    await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id, client.Partner.Size));
                                    await _sender.Send(new UpdateDigimonGradeCommand(client.Partner.Id,
                                        client.Partner.HatchGrade));

                                    client.Tamer.UpdateState(CharacterStateEnum.Loading);
                                    await _sender.Send(new UpdateCharacterStateCommand(client.TamerId,
                                        CharacterStateEnum.Loading));

                                    _mapServer.RemoveClient(client);

                                    client.SetGameQuit(false);
                                    client.Tamer.UpdateSlots();

                                    client.Send(new MapSwapPacket(_configuration[GamerServerPublic],
                                        _configuration[GameServerPort],
                                        client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));
                                }
                                break;

                            case "size":
                                {
                                    var regex = @"(digimon\ssize\s\d){1}";
                                    var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                                    if (!match.Success)
                                    {
                                        client.Send(new SystemMessagePacket(
                                            $"Unknown command. Check the available commands on the Admin Portal."));
                                        break;
                                    }

                                    if (short.TryParse(command[2], out var value))
                                    {
                                        client.Partner.SetSize(value);
                                        client.Partner.SetBaseStatus(
                                            _statusManager.GetDigimonBaseStatus(
                                                client.Partner.CurrentType,
                                                client.Partner.Level,
                                                client.Partner.Size
                                            )
                                        );

                                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                            new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size)
                                                .Serialize());
                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                        await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id, value));
                                    }
                                    else
                                    {
                                        client.Send(
                                            new SystemMessagePacket(
                                                $"Invalid value. Max possible amount is {short.MaxValue}."));
                                    }
                                }
                                break;

                            case "exp":
                                {
                                    var regex =
                                        @"(digimon\sexp\sadd\s\d){1}|(digimon\sexp\sremove\s\d){1}|(digimon\sexp\smax){1}|(digimon\sexp\slevel){1}";
                                    var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                                    if (!match.Success)
                                    {
                                        client.Send(new SystemMessagePacket("Correct usage is \"!digimon exp add value\" or " +
                                                                            "\"!digimon exp remove value\" or " +
                                                                            "\"!digimon exp max\".")
                                            .Serialize());

                                        break;
                                    }

                                    switch (command[2])
                                    {
                                        case "max":
                                            {
                                                if (client.Partner.Level >= (int)GeneralSizeEnum.DigimonLevelMax)
                                                {
                                                    client.Send(new SystemMessagePacket(
                                                        $"Partner already at max level {(int)GeneralSizeEnum.DigimonLevelMax}..."));
                                                    break;
                                                }

                                                var result = _expManager.ReceiveMaxDigimonExperience(client.Partner);

                                                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                                                if (result.Success)
                                                {
                                                    client.Send(new ReceiveExpPacket(0, 0, client.Tamer.CurrentExperience,
                                                        client.Tamer.Partner.GeneralHandler, 0, 0,
                                                        client.Tamer.Partner.CurrentExperience, 0));

                                                    if (result.LevelGain > 0)
                                                    {
                                                        client.Partner.SetBaseStatus(
                                                            _statusManager.GetDigimonBaseStatus(client.Partner.CurrentType,
                                                                client.Partner.Level, client.Partner.Size));

                                                        switch (mapConfig.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _dungeonServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new LevelUpPacket(client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level).Serialize());
                                                                break;
                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new LevelUpPacket(client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level).Serialize());
                                                                break;
                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new LevelUpPacket(client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level).Serialize());
                                                                break;
                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new LevelUpPacket(client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level).Serialize());
                                                                break;
                                                        }

                                                        client.Partner.FullHeal();

                                                        client.Send(new UpdateStatusPacket(client.Tamer));

                                                        switch (mapConfig.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _dungeonServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                                                break;
                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                                                break;
                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                                                break;
                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(client,
                                                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                                                break;
                                                        }
                                                    }

                                                    await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                                }
                                                else
                                                {
                                                    client.Send(new SystemMessagePacket($"No proper configuration for digimon {client.Partner.Model} leveling."));
                                                    return;
                                                }
                                            }
                                            break;

                                        case "add":
                                            {
                                                if (client.Partner.Level == (int)GeneralSizeEnum.DigimonLevelMax)
                                                {
                                                    client.Send(new SystemMessagePacket($"Partner already at max level."));
                                                    break;
                                                }

                                                var value = Convert.ToInt64(command[3]);

                                                var result = _expManager.ReceiveDigimonExperience(value, client.Partner);

                                                if (result.Success)
                                                {
                                                    client.Send(
                                                        new ReceiveExpPacket(
                                                            0,
                                                            0,
                                                            client.Tamer.CurrentExperience,
                                                            client.Tamer.Partner.GeneralHandler,
                                                            value,
                                                            0,
                                                            client.Tamer.Partner.CurrentExperience,
                                                            0
                                                        )
                                                    );
                                                }
                                                else
                                                {
                                                    client.Send(new SystemMessagePacket(
                                                        $"No proper configuration for digimon {client.Partner.Model} leveling."));
                                                    return;
                                                }

                                                if (result.LevelGain > 0)
                                                {
                                                    client.Partner.SetBaseStatus(
                                                        _statusManager.GetDigimonBaseStatus(
                                                            client.Partner.CurrentType,
                                                            client.Partner.Level,
                                                            client.Partner.Size
                                                        )
                                                    );

                                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                                        client.TamerId,
                                                        new LevelUpPacket(
                                                            client.Tamer.Partner.GeneralHandler,
                                                            client.Tamer.Partner.Level
                                                        ).Serialize()
                                                    );

                                                    client.Partner.FullHeal();

                                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                                }

                                                if (result.Success)
                                                    await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                            }
                                            break;

                                        case "remove":
                                            {
                                                var value = Convert.ToInt64(command[3]);

                                                //var digimonInfos = _assets.DigimonLevelInfo.Where(x => x.Type == client.Tamer.Partner.BaseType).ToList();

                                                var digimonInfos = _assets.DigimonLevelInfo.Where(x => x.ScaleType == client.Tamer.Partner.BaseInfo.ScaleType).ToList();

                                                if (digimonInfos == null || !digimonInfos.Any() )//|| digimonInfos.Count != (int)GeneralSizeEnum.DigimonLevelMax)
                                                {
                                                    _logger.Warning(
                                                        $"Incomplete level config for digimon {client.Tamer.Partner.BaseType}.");

                                                    client.Send(new SystemMessagePacket
                                                        ($"No proper configuration for digimon {client.Tamer.Partner.BaseType} leveling."));
                                                    break;
                                                }

                                                //TODO: ajeitar
                                                var partnerInitialLevel = client.Partner.Level;

                                                client.Tamer.LooseExp(value);

                                                client.Send(new ReceiveExpPacket(
                                                    0,
                                                    0,
                                                    client.Tamer.CurrentExperience,
                                                    client.Tamer.Partner.GeneralHandler,
                                                    value * -1,
                                                    0,
                                                    client.Tamer.Partner.CurrentExperience,
                                                    0
                                                ));

                                                if (partnerInitialLevel != client.Partner.Level)
                                                    client.Send(new LevelUpPacket(client.Partner.GeneralHandler,
                                                        client.Partner.Level));

                                                await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                            }
                                            break;

                                        case "level":
                                            {
                                                var value = Convert.ToInt32(command[3]);

                                                client.Partner.SetExp(0);
                                                client.Partner.SetLevel((byte)value);

                                                client.Send(new ReceiveExpPacket(0, 0, client.Tamer.CurrentExperience,
                                                    client.Tamer.Partner.GeneralHandler,
                                                    0, 0, client.Tamer.Partner.CurrentExperience, 0
                                                ));

                                                client.Partner.SetBaseStatus(_statusManager.GetDigimonBaseStatus(client.Partner.CurrentType, client.Partner.Level, client.Partner.Size));

                                                _mapServer.BroadcastForTamerViewsAndSelf(client, new LevelUpPacket(client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.Level).Serialize());

                                                client.Partner.FullHeal();

                                                client.Send(new UpdateStatusPacket(client.Tamer));

                                                await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                            }
                                            break;

                                        default:
                                            {
                                                client.Send(new SystemMessagePacket(
                                                    "Correct usage is \"!digimon exp add value\" or " +
                                                    "\"!digimon exp max\"."));
                                            }
                                            break;
                                    }
                                }
                                break;

                            default:
                                {
                                    client.Send(
                                        new SystemMessagePacket(
                                            "Unknown command. Check the available commands at the admin portal."));
                                }
                                break;
                        }
                    }
                    break;

                case "dc":
                    {
                        var regex = @"^dc\s[\w\s]+$";
                        var match = Regex.Match(message, regex, RegexOptions.None);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !dc TamerName"));
                            break;
                        }

                        string[] comando = message.Split(' ');
                        var TamerName = comando[1];

                        var targetClient = _mapServer.FindClientByTamerName(TamerName);
                        var targetClientD = _dungeonServer.FindClientByTamerName(TamerName);

                        if (targetClient == null && targetClientD == null)
                        {
                            client.Send(new SystemMessagePacket($"Player {TamerName} not Online!"));
                            break;
                        }

                        if (targetClient == null) targetClient = targetClientD;

                        if (client.Tamer.Name == TamerName)
                        {
                            client.Send(new SystemMessagePacket($"You are a {TamerName}!"));
                            break;
                        }

                        targetClient.Send(new SystemMessagePacket($"Voce foi kickado pela staff!"));
                        targetClient.Disconnect();
                    }
                    break;

                case "loadmonster":
                    {
                        try
                        {
                            if (client.Tamer.AccountId != 911)
                            {
                                break;
                            }

                            _logger.Information($"Received command: {message}");

                            var regex = @"^loadmonster\s\d+$"; // Match only "!loadmonster <SummonId>"
                            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                            if (!match.Success)
                            {
                                client.Send(new SystemMessagePacket($"Unknown command.\nType !loadmonster SummonId"));
                                break;
                            }

                            // Split command into parts
                            var commandParts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                            // Ensure at least 2 parameters (!su SummonId)
                            if (commandParts.Length < 2 || !int.TryParse(commandParts[1], out var summonId))
                            {
                                client.Send(new SystemMessagePacket($"Invalid Summon ID !!"));
                                break;
                            }

                            _logger.Information($"Parsed Summon ID: {summonId}");

                            // Find summon info based on Summon ID
                            var summonInfo = _assets.SummonInfo.FirstOrDefault(x => x.Id == summonId);
                            if (summonInfo == null)
                            {
                                client.Send(new SystemMessagePacket($"Invalid Summon ID !!"));
                                break;
                            }

                            foreach (var mobToAdd in summonInfo?.SummonedMobs ?? Enumerable.Empty<SummonMobModel>())
                            {
                                var mob = (SummonMobModel)mobToAdd.Clone();
                                var matchingMaps = _mapServer.Maps.Where(x => x.MapId == mob.Location.MapId).ToList();

                                foreach (var map in matchingMaps)
                                {
                                    if (map.SummonMobs.Any(existingMob => existingMob.Id == mob.Id))
                                    {
                                        continue;
                                    }

                                    mob.TamersViewing.Clear();
                                    mob.Reset();
                                    mob.SetRespawn();
                                    mob.SetId(mob.Id);
                                    mob.SetLocation(mob.Location.MapId, mob.Location.X, mob.Location.Y);
                                    mob.SetDuration();
                                    _mapServer.AddSummonMobs(mob.Location.MapId, mob);

                                    _logger.Information($"Mob {mob.Type} : {mob.Name} spawned from Summon ID {summonId}!");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error spawning mob: {ex.Message}");
                        }
                    }
                    break;

                case "gfstorage":
                    {
                        var regex = @"^(gfstorage\s(add\s\d{1,7}\s\d{1,4}|clear))$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command.\nType: gfstorage add itemId Amount or gfstorage clear"));
                            break;
                        }

                        if (command[1].ToLower() == "clear")
                        {
                            client.Tamer.GiftWarehouse.Clear();

                            client.Send(new SystemMessagePacket($" GiftStorage slots cleaned."));
                            client.Send(new LoadGiftStoragePacket(client.Tamer.GiftWarehouse));

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                        }
                        else if (command[1].ToLower() == "add")
                        {
                            if (!int.TryParse(command[2], out var itemId))
                            {
                                client.Send(new SystemMessagePacket($"Invalid ItemID format."));
                                break;
                            }

                            var amount = command.Length == 3
                                ? 1
                                : (int.TryParse(command[3], out var parsedAmount) ? parsedAmount : 1);

                            var newItem = new ItemModel();
                            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                            if (newItem.ItemInfo == null)
                            {
                                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                                break;
                            }

                            newItem.ItemId = itemId;
                            newItem.Amount = amount;

                            if (newItem.IsTemporary)
                                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                            newItem.EndDate = DateTime.Now.AddDays(7);

                            if (client.Tamer.GiftWarehouse.AddItemGiftStorage(newItem))
                            {
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                                client.Send(new SystemMessagePacket($"Added x{amount} item {itemId} to GiftStorage."));
                            }
                            else
                            {
                                client.Send(
                                    new SystemMessagePacket(
                                        $"Could not add item {itemId} to GiftStorage. Slots may be full."));
                            }
                        }
                    }
                    break;

                case "cashstorage":
                    {
                        var regex = @"^(cashstorage\s(add\s\d{1,7}\s\d{1,4}|clear))$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket(
                                $"Unknown command.\nType: cashstorage add itemId Amount or cashstorage clear"));
                            break;
                        }

                        if (command[1].ToLower() == "clear")
                        {
                            client.Tamer.AccountCashWarehouse.Clear();

                            client.Send(new SystemMessagePacket($" CashStorage slots cleaned."));
                            client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));
                        }
                        else if (command[1].ToLower() == "add")
                        {
                            if (!int.TryParse(command[2], out var itemId))
                            {
                                client.Send(new SystemMessagePacket($"Invalid ItemID format."));
                                break;
                            }

                            var amount = command.Length == 3
                                ? 1
                                : (int.TryParse(command[3], out var parsedAmount) ? parsedAmount : 1);

                            var newItem = new ItemModel();
                            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                            if (newItem.ItemInfo == null)
                            {
                                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                                break;
                            }

                            newItem.ItemId = itemId;
                            newItem.Amount = amount;

                            if (newItem.IsTemporary)
                                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                            var itemClone = (ItemModel)newItem.Clone();

                            if (client.Tamer.AccountCashWarehouse.AddItemGiftStorage(newItem))
                            {
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));
                                client.Send(new SystemMessagePacket($"Added item {itemId} x{amount} to CashStorage."));
                            }
                            else
                            {
                                client.Send(
                                    new SystemMessagePacket(
                                        $"Could not add item {itemId} to CashStorage. Slots may be full."));
                            }
                        }
                    }
                    break;

                case "hide":
                    {
                        var regex = @"(hide$){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        if (client.Tamer.Hidden)
                        {
                            client.Send(new SystemMessagePacket($"You are already in hide mode."));
                        }
                        else
                        {
                            client.Tamer.SetHidden(true);
                            client.Send(new SystemMessagePacket($"View state has been set to hide mode."));
                        }
                    }
                    break;

                case "show":
                    {
                        var regex = @"(show$){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        if (client.Tamer.Hidden)
                        {
                            client.Tamer.SetHidden(false);
                            client.Send(new SystemMessagePacket($"View state has been set to show mode."));
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"You are already in show mode."));
                        }
                    }
                    break;

                case "inv":
                    {
                        var regex = @"^(inv\s+(add\s+\d{1,3}|clear))$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !inv add or !inv clear"));
                            break;
                        }

                        if (command[1].ToLower() == "add")
                        {
                            if (byte.TryParse(command[2], out byte targetSize) && targetSize > 0)
                            {
                                var newSize = client.Tamer.Inventory.AddSlots(targetSize);

                                client.Send(new SystemMessagePacket($"Inventory slots updated to {newSize}."));
                                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                                var newSlots = client.Tamer.Inventory.Items.Where(x => x.ItemList == null).ToList();
                                await _sender.Send(new AddInventorySlotsCommand(newSlots));
                                newSlots.ForEach(newSlot =>
                                {
                                    newSlot.ItemList = client.Tamer.Inventory.Items.First(x => x.ItemList != null)
                                        .ItemList;
                                });
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket(
                                    $"Invalid command parameters. Check the available commands on the Admin Portal."));
                                break;
                            }
                        }
                        else if (command[1].ToLower() == "clear")
                        {
                            client.Tamer.Inventory.Clear();

                            client.Send(new SystemMessagePacket($"Inventory slots cleaned."));
                            client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }
                    }
                    break;

                case "storage":
                    {
                        var regex = @"^(storage\s+(add\s+\d{1,3}|clear))$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !storage add or !storage clear"));
                            break;
                        }

                        if (command[1].ToLower() == "add")
                        {
                            if (byte.TryParse(command[2], out byte targetSize) && targetSize > 0)
                            {
                                var newSize = client.Tamer.Warehouse.AddSlots(targetSize);

                                client.Send(new SystemMessagePacket($"Inventory slots updated to {newSize}."));
                                client.Send(new LoadInventoryPacket(client.Tamer.Warehouse, InventoryTypeEnum.Warehouse));

                                var newSlots = client.Tamer.Warehouse.Items.Where(x => x.ItemList == null).ToList();
                                await _sender.Send(new AddInventorySlotsCommand(newSlots));
                                newSlots.ForEach(newSlot =>
                                {
                                    newSlot.ItemList = client.Tamer.Warehouse.Items.First(x => x.ItemList != null)
                                        .ItemList;
                                });
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket(
                                    $"Invalid command parameters. Check the available commands on the Admin Portal."));
                                break;
                            }
                        }
                        else if (command[1].ToLower() == "clear")
                        {
                            client.Tamer.Warehouse.Clear();

                            client.Send(new SystemMessagePacket($"Storage slots cleaned."));
                            client.Send(new LoadInventoryPacket(client.Tamer.Warehouse, InventoryTypeEnum.Warehouse));

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                        }
                    }
                    break;

                case "unlockevos":
                    {
                        var regex = @"^unlockevos";
                        var match = Regex.IsMatch(message, regex, RegexOptions.IgnoreCase);

                        if (!match)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !unlockevos"));
                            break;
                        }

                        // Unlock Digimon Evolutions

                        foreach (var evolution in client.Partner.Evolutions)
                        {
                            evolution.Unlock();
                            await _sender.Send(new UpdateEvolutionCommand(evolution));
                        }

                        // Unlock Digimon Evolutions on Encyclopedia

                        var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?.Lines
                            .FirstOrDefault(x => x.Type == client.Partner.CurrentType);

                        if (evoInfo == null)
                        {
                            _logger.Error($"evoInfo not found !! [ Unlockevos Command ]");
                        }
                        else
                        {
                            var encyclopedia =
                                client.Tamer.Encyclopedia.FirstOrDefault(x => x.DigimonEvolutionId == evoInfo.EvolutionId);

                            if (encyclopedia == null)
                            {
                                _logger.Error($"encyclopedia not found !! [ Unlockevos Command ]");
                            }
                            else
                            {
                                foreach (var evolution in client.Partner.Evolutions)
                                {
                                    var encyclopediaEvolution =
                                        encyclopedia.Evolutions.First(x => x.DigimonBaseType == evolution.Type);

                                    encyclopediaEvolution.Unlock();

                                    await _sender.Send(
                                        new UpdateCharacterEncyclopediaEvolutionsCommand(encyclopediaEvolution));
                                }

                                int LockedEncyclopediaCount = encyclopedia.Evolutions.Count(x => x.IsUnlocked == false);

                                if (LockedEncyclopediaCount <= 0)
                                {
                                    try
                                    {
                                        encyclopedia.SetRewardAllowed();
                                        await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
                                    }
                                    catch (Exception ex)
                                    {
                                        //_logger.Error($"LockedEncyclopediaCount Error:\n{ex.Message}");
                                    }
                                }
                            }
                        }

                        // -- RELOADING MAP -----------------------------------------------------------------------------

                        client.Tamer.UpdateState(CharacterStateEnum.Loading);
                        await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                        _mapServer.RemoveClient(client);

                        client.SetGameQuit(false);
                        client.Tamer.UpdateSlots();

                        client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                            client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));
                    }
                    break;

                case "openseals":
                    {
                        var sealInfoList = _assets.SealInfo;
                        foreach (var seal in sealInfoList)
                        {
                            client.Tamer.SealList.AddOrUpdateSeal(seal.SealId, 3000, seal.SequentialId);
                        }

                        client.Partner?.SetSealStatus(sealInfoList);

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        await _sender.Send(new UpdateCharacterSealsCommand(client.Tamer.SealList));

                        client.Tamer.UpdateState(CharacterStateEnum.Loading);
                        await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                        _mapServer.RemoveClient(client);

                        client.SetGameQuit(false);

                        client.Send(new MapSwapPacket(
                            _configuration[GamerServerPublic],
                            _configuration[GameServerPort],
                            client.Tamer.Location.MapId,
                            client.Tamer.Location.X,
                            client.Tamer.Location.Y));
                    }
                    break;

                case "su":
                    {
                        var regex = @"^su\s\d+(\s\d+)?$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !su MobType"));
                            break;
                        }

                        var mobType = int.Parse(command[1].ToLower());

                        var MobInfo = _assets.SummonMobInfo.FirstOrDefault(x => x.Type == mobType);

                        if (MobInfo != null)
                        {
                            var mob = (SummonMobModel)MobInfo.Clone();

                            //_logger.Information($"mob {mob.Id} : {mob.Type} : {mob.Name} being summoned !!");

                            try
                            {
                                int radius = 500;
                                var random = new Random();

                                int xOffset = random.Next(-radius, radius + 1);
                                int yOffset = random.Next(-radius, radius + 1);

                                int bossX = client.Tamer.Location.X + xOffset;
                                int bossY = client.Tamer.Location.Y + yOffset;

                                var map = _mapServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

                                if (map == null)
                                {
                                    client.Send(new SystemMessagePacket($"Map not found !!"));
                                    break;
                                }

                                //var mobId = mob.Id;
                                var mobId = map.SummonMobs.Count + 1;

                                mob.SetId(mobId);
                                mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                                mob.SetDuration();
                                mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                                _mapServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.Tamer.Channel);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error($"{ex.Message}");
                            }
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"Summon with Type {mobType} not found on [Config].[SummonMob]!!"));
                        }
                    }
                    break;

                case "summon":
                    {
                        var regex = @"(summon\s\d\s\d){1}|(summon\s\d){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !summon (itemId)"));
                            break;
                        }

                        if (command.Length > 2)
                        {
                            client.Send(new SystemMessagePacket("Invalid command.\nType !summon (itemId)"));
                            break;
                        }

                        var itemId = int.Parse(command[1].ToLower());

                        var SummonInfo = _assets.SummonInfo.FirstOrDefault(x => x.ItemId == itemId);

                        if (SummonInfo != null)
                        {
                            await NewSummon(client, SummonInfo);
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"Invalid CardId !!"));
                        }
                    }
                    break;

                case "heal":
                    {
                        var regex = @"^heal\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !heal"));
                            break;
                        }

                        client.Tamer.FullHeal();
                        client.Tamer.Partner.FullHeal();

                        client.Send(new UpdateStatusPacket(client.Tamer));
                        await _sender.Send(new UpdateCharacterBasicInfoCommand(client.Tamer));
                    }
                    break;

                case "stats":
                    {
                        var regex = @"^stats\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !stats"));
                            break;
                        }

                        client.Send(new SystemMessagePacket($"Critical Damage: {client.Tamer.Partner.CD / 100}%\n" +
                                                            $"Attribute Damage: {client.Tamer.Partner.ATT / 100}%\n" +
                                                            $"Digimon SKD: {client.Tamer.Partner.SKD}\n" +
                                                            $"Digimon SCD: {client.Tamer.Partner.SCD / 100}%\n" +
                                                            $"Tamer BonusEXP: {client.Tamer.BonusEXP}%\n" +
                                                            $"Tamer Move Speed: {client.Tamer.MS}", ""));
                    }
                    break;

                case "encyclopedia":
                    {
                        var regex = @"(encyclopedia\s\d\s\d){1}|(encyclopedia\s\d){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !encyclopedia"));
                            break;
                        }

                        int type = int.Parse(command[1].ToLower());
                        // DigimonBaseInfoAssetModel digimon = _mapper.Map<DigimonBaseInfoAssetModel>(await _sender.Send(new DigimonBaseInfoQuery(type)));

                        var digimonEvolutionInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == type);

                        _logger.Information($"type: {type}, info: {digimonEvolutionInfo?.ToString()}");

                        if (digimonEvolutionInfo == null)
                        {
                            client.Send(new SystemMessagePacket($"evolution info not found"));
                            return;
                        }

                        List<EvolutionLineAssetModel> evolutionLines =
                            digimonEvolutionInfo.Lines.OrderBy(x => x.Id).ToList();

                        var encyclopedia = CharacterEncyclopediaModel.Create(client.TamerId, digimonEvolutionInfo.Id, 120,
                            14000, 15, 15, 15, 15, 15, false, false);

                        evolutionLines?.ForEach(x =>
                        {
                            encyclopedia.Evolutions.Add(
                                CharacterEncyclopediaEvolutionsModel.Create(x.Type, x.SlotLevel, false));
                        });

                        client.Tamer.Encyclopedia.Add(encyclopedia);

                        var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));

                        client.Send(new SystemMessagePacket($"Encyclopedia added! {encyclopediaAdded.Id}, evolutions"));
                    }
                    break;

                // -- TOOLS --------------------------------------

                #region Tools

                case "tools":
                    {
                        var regex = @"^tools\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !tools"));
                            break;
                        }

                        client.Send(new SystemMessagePacket($"Tools Commands:", "").Serialize());
                        client.Send(
                            new SystemMessagePacket($"1. !fullacc\n2. !evopack\n3. !spacepack\n4. !clon (type) (value)", "")
                                .Serialize());
                    }
                    break;

                case "fullacc":
                    {
                        await AddItemToInventory(client, 50, 1);    // GM CLoth
                        await AddItemToInventory(client, 51, 1);  // GM Sponsor
                        await AddItemToInventory(client, 1, 1);     // Purple Vice
                        await AddItemToInventory(client, 534, 1);  // Jogress Chip
                        await AddItemToInventory(client, 40057, 1); // XAI Ver VI
                        await AddItemToInventory(client, 443, 1);  // DigiAuraBox

                        #region BITS (100T)

                        client.Tamer.Inventory.AddBits(100000000);

                        client.Send(
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());

                        await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory.Id,
                            client.Tamer.Inventory.Bits));

                        #endregion
                    }
                    break;

                case "evopack":
                    {
                        var regex = @"^evopack\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !evopack"));
                            break;
                        }

                        await AddItemToInventory(client, 41002, 999); // Accelerator
                        await AddItemToInventory(client, 41000, 999); // Spirit Accelerator
                        await AddItemToInventory(client, 5001, 999);  // Evoluter
                        await AddItemToInventory(client, 71594, 999); // X-Antibody

                        client.Send(
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());
                        client.Send(new SystemMessagePacket($"Items for evo on inventory!!"));
                    }
                    break;

                case "spacepack":
                    {
                        var regex = @"^spacepack\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !spacepack"));
                            break;
                        }

                        await AddItemToInventory(client, 5507, 120); // Inventory Expansion
                        await AddItemToInventory(client, 5508, 120); // Warehouse Expansion
                        await AddItemToInventory(client, 5004, 50); // Archive Expansion
                        await AddItemToInventory(client, 5812, 2); // Digimon Slot

                        client.Send(
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());
                        client.Send(new SystemMessagePacket($"Items for space on inventory!!"));
                    }
                    break;

                case "hatchpack":
                    {
                        var regex = @"^hatchpack\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !hatchpack"));
                            break;
                        }

                        await AddItemToInventory(client, 97002, 999);
                        await AddItemToInventory(client, 97012, 999);
                        await AddItemToInventory(client, 97022, 999);
                        await AddItemToInventory(client, 97032, 999);
                        await AddItemToInventory(client, 97042, 999);
                        await AddItemToInventory(client, 97052, 999);
                        await AddItemToInventory(client, 97062, 999);
                        await AddItemToInventory(client, 97072, 999);

                        client.Send(
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());
                        client.Send(new SystemMessagePacket($"Items for space on inventory!!"));
                    }
                    break;

                case "clon":
                    {
                        var cloneAT = (DigicloneTypeEnum)1;
                        var cloneBL = (DigicloneTypeEnum)2;
                        var cloneCT = (DigicloneTypeEnum)3;
                        var cloneEV = (DigicloneTypeEnum)5;
                        var cloneHP = (DigicloneTypeEnum)7;

                        if (command.Length < 2)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !clon type value"));
                            break;
                        }

                        int maxCloneLevel = 15;

                        if (command.Length > 2 && int.TryParse(command[2], out int requestedLevel))
                        {
                            maxCloneLevel = Math.Min(requestedLevel, 15);
                        }

                        async Task IncreaseCloneLevel(DigicloneTypeEnum cloneType, string cloneName)
                        {
                            var currentCloneLevel = client.Partner.Digiclone.GetCurrentLevel(cloneType);

                            while (currentCloneLevel < maxCloneLevel)
                            {
                                var cloneAsset = _assets.CloneValues.FirstOrDefault(x =>
                                    x.Type == cloneType && currentCloneLevel + 1 >= x.MinLevel &&
                                    currentCloneLevel + 1 <= x.MaxLevel);

                                if (cloneAsset != null)
                                {
                                    var cloneResult = DigicloneResultEnum.Success;
                                    short value = (short)cloneAsset.MaxValue;

                                    client.Partner.Digiclone.IncreaseCloneLevel(cloneType, value);

                                    client.Send(new DigicloneResultPacket(cloneResult, client.Partner.Digiclone));
                                    client.Send(new UpdateStatusPacket(client.Tamer));

                                    await _sender.Send(new UpdateDigicloneCommand(client.Partner.Digiclone));

                                    currentCloneLevel++;
                                    _logger.Verbose($"New {cloneName} Clon Level: {currentCloneLevel}");
                                }
                                else
                                {
                                    break;
                                }
                            }

                            client.Send(new SystemMessagePacket($"New {cloneName} Clon Level: {currentCloneLevel}"));
                        }

                        switch (command[1].ToLower())
                        {
                            case "at":
                                {
                                    await IncreaseCloneLevel(cloneAT, "AT");
                                }
                                break;

                            case "bl":
                                {
                                    await IncreaseCloneLevel(cloneBL, "BL");
                                }
                                break;

                            case "ct":
                                {
                                    await IncreaseCloneLevel(cloneCT, "CT");
                                }
                                break;

                            case "hp":
                                {
                                    await IncreaseCloneLevel(cloneHP, "HP");
                                }
                                break;

                            case "ev":
                                {
                                    await IncreaseCloneLevel(cloneEV, "EV");
                                }
                                break;

                            default:
                                {
                                    client.Send(new SystemMessagePacket("Unknown command.\nType !clon type value"));
                                }
                                break;
                        }
                    }
                    break;

                case "maptamers":
                    {
                        var regex = @"^maptamers\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !maptamers"));
                            break;
                        }

                        var mapTamers =
                            _mapServer.Maps.FirstOrDefault(x =>
                                x.Clients.Exists(gameClient => gameClient.TamerId == client.Tamer.Id));

                        if (mapTamers != null)
                        {
                            client.Send(new SystemMessagePacket($"Total Tamers in Map: {mapTamers.ConnectedTamers.Count}",
                                ""));
                        }
                        else
                        {
                            mapTamers = _dungeonServer.Maps.FirstOrDefault(x =>
                                x.Clients.Exists(gameClient => gameClient.TamerId == client.Tamer.Id));

                            client.Send(
                                new SystemMessagePacket($"Total Tamers in Dungeon Map: {mapTamers.ConnectedTamers.Count}",
                                    ""));
                        }
                    }
                    break;

                #endregion

                // -- INFO ---------------------------------------

                #region INFO

                case "deckload":
                    {
                        var regex = @"^deckload\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !deckload"));
                            break;
                        }

                        var evolution = client.Partner.Evolutions[0];

                        _logger.Information(
                            $"Evolution ID: {evolution.Id} | Evolution Type: {evolution.Type} | Evolution Unlocked: {evolution.Unlocked}");

                        var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?.Lines
                            .FirstOrDefault(x => x.Type == evolution.Type);

                        _logger.Information($"EvoInfo ID: {evoInfo.Id}");
                        _logger.Information($"EvoInfo EvolutionId: {evoInfo.EvolutionId}");

                        // --- CREATE DB ----------------------------------------------------------------------------------------

                        var digimonEvolutionInfo = _assets.EvolutionInfo.First(x => x.Type == client.Partner.BaseType);

                        var digimonEvolutions = client.Partner.Evolutions;

                        var encyclopediaExists =
                            client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo.Id);

                        if (!encyclopediaExists)
                        {
                            if (digimonEvolutionInfo != null)
                            {
                                var newEncyclopedia = CharacterEncyclopediaModel.Create(client.TamerId,
                                    digimonEvolutionInfo.Id, client.Partner.Level, client.Partner.Size, 0, 0, 0, 0, 0,
                                    false, false);

                                digimonEvolutions?.ForEach(x =>
                                {
                                    var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);

                                    byte slotLevel = 0;

                                    if (evolutionLine != null)
                                        slotLevel = evolutionLine.SlotLevel;

                                    newEncyclopedia.Evolutions.Add(
                                        CharacterEncyclopediaEvolutionsModel.Create(newEncyclopedia.Id, x.Type, slotLevel,
                                            Convert.ToBoolean(x.Unlocked)));
                                });

                                var encyclopediaAdded =
                                    await _sender.Send(new CreateCharacterEncyclopediaCommand(newEncyclopedia));

                                client.Tamer.Encyclopedia.Add(encyclopediaAdded);

                                _logger.Information($"Digimon Type {client.Partner.BaseType} encyclopedia created !!");
                            }
                        }
                        else
                        {
                            _logger.Information($"Encyclopedia already exist !!");
                        }

                        // --- UNLOCK -------------------------------------------------------------------------------------------

                        var encyclopedia =
                            client.Tamer.Encyclopedia.First(x => x.DigimonEvolutionId == evoInfo.EvolutionId);

                        _logger.Information($"Encyclopedia is: {encyclopedia.Id}, evolution id: {evoInfo.EvolutionId}");

                        if (encyclopedia != null)
                        {
                            var encyclopediaEvolution =
                                encyclopedia.Evolutions.First(x => x.DigimonBaseType == evolution.Type);

                            if (!encyclopediaEvolution.IsUnlocked)
                            {
                                encyclopediaEvolution.Unlock();

                                await _sender.Send(new UpdateCharacterEncyclopediaEvolutionsCommand(encyclopediaEvolution));

                                int LockedEncyclopediaCount = encyclopedia.Evolutions.Count(x => x.IsUnlocked == false);

                                if (LockedEncyclopediaCount <= 0)
                                {
                                    encyclopedia.SetRewardAllowed();
                                    await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
                                }
                            }
                            else
                            {
                                _logger.Information($"Evolution already unlocked on encyclopedia !!");
                            }
                        }

                        // ------------------------------------------------------------------------------------------------------

                        client.Send(new SystemMessagePacket($"Encyclopedia verifyed and updated !!"));
                    }
                    break;

                #endregion

                // -- MAINTENANCE --------------------------------

                #region Maintenance

                case "live":
                    {
                        if (client.AccessLevel == AccountAccessLevelEnum.Administrator)
                        {
                            var packet = new PacketWriter();
                            packet.Type(1006);
                            packet.WriteByte(10);
                            packet.WriteByte(1);
                            packet.WriteString("Server is now on live!");
                            packet.WriteByte(0);

                            _mapServer.BroadcastGlobal(packet.Serialize());

                            var server = await _sender.Send(new GetServerByIdQuery(client.ServerId));

                            if (server.Register != null)
                                await _sender.Send(new UpdateServerCommand(server.Register.Id, server.Register.Name,
                                    server.Register.Experience, false, server.Register.ExperienceBurn, server.Register.ExperienceType));
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket("Insufficient permission level for this action."));
                        }
                    }
                    break;

                case "maintenance":
                    {
                        if (client.AccessLevel == AccountAccessLevelEnum.Administrator)
                        {
                            var packet = new PacketWriter();
                            packet.Type(1006);
                            packet.WriteByte(10);
                            packet.WriteByte(1);
                            packet.WriteString("Server shutdown for maintenance in 2 minutes");
                            packet.WriteByte(0);

                            _mapServer.BroadcastGlobal(packet.Serialize());

                            var ServerId = client.ServerId;
                            var server = await _sender.Send(new GetServerByIdQuery(client.ServerId));

                            if (server.Register != null)
                                await _sender.Send(new UpdateServerCommand(server.Register.Id, server.Register.Name,
                                    server.Register.Experience, true, server.Register.ExperienceBurn, server.Register.ExperienceType));

                            Task task = Task.Run(async () =>
                            {
                                Thread.Sleep(60000);
                                var packetWriter = new PacketWriter();
                                packetWriter.Type(1006);
                                packetWriter.WriteByte(10);
                                packetWriter.WriteByte(1);
                                packetWriter.WriteString("Server shutdown for maintenance in 60s");
                                packetWriter.WriteByte(0);
                                _mapServer.BroadcastGlobal(packetWriter.Serialize());
                                _dungeonServer.BroadcastGlobal(packetWriter.Serialize());

                                Thread.Sleep(30000);
                                packetWriter = new PacketWriter();
                                packetWriter.Type(1006);
                                packetWriter.WriteByte(10);
                                packetWriter.WriteByte(1);
                                packetWriter.WriteString("Server shutdown for maintenance in 30s");
                                packetWriter.WriteByte(0);
                                _mapServer.BroadcastGlobal(packetWriter.Serialize());
                                _dungeonServer.BroadcastGlobal(packetWriter.Serialize());

                                Thread.Sleep(20000);
                                for (int i = 10; i >= 0; i--)
                                {
                                    Thread.Sleep(1000);
                                    packetWriter = new PacketWriter();
                                    packetWriter.Type(1006);
                                    packetWriter.WriteByte(10);
                                    packetWriter.WriteByte(1);
                                    packetWriter.WriteString($"Server shutdown for maintenance in {i}s");
                                    packetWriter.WriteByte(0);

                                    _mapServer.BroadcastGlobal(packetWriter.Serialize());
                                    _dungeonServer.BroadcastGlobal(packetWriter.Serialize());
                                }

                                var currentServer = await _sender.Send(new GetServerByIdQuery(ServerId));
                                if (currentServer.Register.Maintenance)
                                {
                                    _mapServer.BroadcastGlobal(new DisconnectUserPacket("Server maintenance").Serialize());
                                    _dungeonServer.BroadcastGlobal(
                                        new DisconnectUserPacket("Server maintenance").Serialize());
                                }
                            });
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket("Insufficient permission level for this action."));
                        }
                    }
                    break;

                #endregion

                // -- MESSAGE ------------------------------------

                #region Messages

                case "notice":
                    {
                        var notice = string.Join(" ", message.Split(' ').Skip(1));
                        var packet = new PacketWriter();
                        packet.Type(1006);
                        packet.WriteByte(10);
                        packet.WriteByte(1);
                        packet.WriteString($"{notice}");
                        packet.WriteByte(0);

                        _mapServer.BroadcastGlobal(packet.Serialize());
                    }
                    break;

                case "ann":
                    {
                        var notice = string.Join(" ", message.Split(' ').Skip(1));
                        _dungeonServer.BroadcastGlobal(
                            new ChatMessagePacket(notice, ChatTypeEnum.Megaphone, "[System]", 52, 120).Serialize());
                        _mapServer.BroadcastGlobal(
                            new ChatMessagePacket(notice, ChatTypeEnum.Megaphone, "[System]", 52, 120).Serialize());
                    }
                    break;

                #endregion

                // -- LOCATION -----------------------------------

                #region Location

                case "where":
                    {
                        var regex = @"(where$){1}|(location$){1}|(position$){1}|(pos$){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        var loc = client.Tamer.Location;
                        var ch = client.Tamer.Channel;

                        var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(loc.MapId));

                        client.Send(
                            new SystemMessagePacket(
                                $"Map {loc.MapId} Ch {ch} (X: {loc.X}, Y: {loc.Y})\nServer: {mapConfig.Type}"));
                    }
                    break;

                case "tp":
                    {
                        var regex = @"(tp\s\d\s\d){1}|(tp\s\d){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        var playerMap = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                        try
                        {
                            var mapId = Convert.ToInt32(command[1].ToLower());
                            var waypoint = command.Length == 3 ? Convert.ToInt32(command[2]) : 0;

                            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(mapId));
                            var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));

                            if (mapConfig == null)
                            {
                                client.Send(new SystemMessagePacket($"Config Map not found for MapID: {mapId}"));
                                break;
                            }
                            else if (waypoints == null || !waypoints.Regions.Any())
                            {
                                client.Send(
                                    new SystemMessagePacket($"Map Region information not found for MapID: {mapId}"));
                                break;
                            }

                            switch (playerMap.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.RemoveClient(client);
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.RemoveClient(client);
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.RemoveClient(client);
                                    break;
                                case MapTypeEnum.Default:
                                    _mapServer.RemoveClient(client);
                                    break;
                            }

                            var destination = waypoints.Regions.First();

                            client.Tamer.NewLocation(mapId, destination.X, destination.Y);
                            await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                            client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                            await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                            client.Tamer.UpdateState(CharacterStateEnum.Loading);
                            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));
                            client.SetGameQuit(false);

                            client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                                client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y).Serialize());
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"TP Error:\n {ex.Message}");
                        }

                        var party = _partyManager.FindParty(client.TamerId);

                        if (party != null)
                        {
                            party.UpdateMember(party[client.TamerId], client.Tamer);

                            /*foreach (var target in party.Members.Values)
                            {
                                var targetClient = _mapServer.FindClientByTamerId(target.Id);

                                if (targetClient == null) targetClient = _dungeonServer.FindClientByTamerId(target.Id);

                                if (targetClient == null) continue;

                                if (target.Id != client.Tamer.Id)
                                    targetClient.Send(new PartyMemberWarpGatePacket(party[client.TamerId], targetClient.Tamer).Serialize());
                            }*/

                            _mapServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _dungeonServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _eventServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _pvpServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());
                        }
                    }
                    break;

                case "tpto":
                    {
                        var regex = @"^tpto\s[\w\s]+$";
                        var match = Regex.Match(message, regex, RegexOptions.None);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !tpto (TamerName)"));
                            break;
                        }

                        string[] comando = message.Split(' ');
                        var TamerName = comando[1];

                        GameClient? targetClient;

                        var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                        if (client.Tamer.Name == TamerName)
                        {
                            client.Send(new SystemMessagePacket($"You can't teleport to yourself!"));
                            break;
                        }

                        var map = _mapServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.Tamer.Name == TamerName));
                        var mapD = _dungeonServer.Maps.FirstOrDefault(x =>
                            x.Clients.Exists(x => x.Tamer.Name == TamerName));
                        var mapE = _eventServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.Tamer.Name == TamerName));
                        var mapP = _pvpServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.Tamer.Name == TamerName));

                        if (map != null)
                        {
                            targetClient = _mapServer.FindClientByTamerName(TamerName);
                        }
                        else if (mapD != null)
                        {
                            targetClient = _dungeonServer.FindClientByTamerName(TamerName);
                        }
                        else if (mapE != null)
                        {
                            targetClient = _eventServer.FindClientByTamerName(TamerName);
                        }
                        else if (mapP != null)
                        {
                            targetClient = _pvpServer.FindClientByTamerName(TamerName);
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"Player {TamerName} not found !"));
                            break;
                        }

                        switch (mapConfig.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.RemoveClient(client);
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.RemoveClient(client);
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.RemoveClient(client);
                                break;
                            case MapTypeEnum.Default:
                                _mapServer.RemoveClient(client);
                                break;
                        }

                        var destination = targetClient.Tamer.Location;

                        client.Tamer.SetTamerTP(targetClient.TamerId);
                        await _sender.Send(new ChangeTamerIdTPCommand(client.Tamer.Id, (int)targetClient.TamerId));

                        client.Tamer.NewLocation(destination.MapId, destination.X, destination.Y);
                        await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                        client.Tamer.Partner.NewLocation(destination.MapId, destination.X, destination.Y);
                        await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                        client.Tamer.SetCurrentChannel(targetClient.Tamer.Channel);

                        client.Tamer.UpdateState(CharacterStateEnum.Loading);
                        await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                        client.SetGameQuit(false);

                        client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                            client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y).Serialize());

                        var party = _partyManager.FindParty(client.TamerId);

                        if (party != null)
                        {
                            party.UpdateMember(party[client.TamerId], client.Tamer);

                            /*party.Members.Values.Where(x => x.Id != client.TamerId).ToList().ForEach(member =>
                            {
                                _dungeonServer.BroadcastForUniqueTamer(member.Id, new PartyMemberWarpGatePacket(party[client.TamerId], member).Serialize());
                            });*/

                            _mapServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _dungeonServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _eventServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                            _pvpServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());
                        }
                    }
                    break;

                case "tptamer":
                    {
                        var regex = @"^tptamer\s[\w\s]+$";
                        var match = Regex.Match(message, regex, RegexOptions.None);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !tptamer (TamerName)"));
                            break;
                        }

                        if (command.Length < 2)
                        {
                            client.Send(new SystemMessagePacket("Invalid command format.\nType !tptamer (TamerName)"));
                            break;
                        }

                        var tamerName = command[1];

                        GameClient? TargetSummon;

                        var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                        switch (mapConfig!.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                TargetSummon = _dungeonServer.FindClientByTamerName(tamerName);
                                break;
                            case MapTypeEnum.Event:
                                TargetSummon = _eventServer.FindClientByTamerName(tamerName);
                                break;
                            case MapTypeEnum.Pvp:
                                TargetSummon = _pvpServer.FindClientByTamerName(tamerName);
                                break;
                            default:
                                TargetSummon = _mapServer.FindClientByTamerName(tamerName);
                                break;
                        }

                        if (TargetSummon == null)
                        {
                            client.Send(new SystemMessagePacket($"Tamer {tamerName} is not online!"));
                            return;
                        }

                        _logger.Information($"GM: {client.Tamer.Name} teleported Tamer: {TargetSummon.Tamer.Name}");

                        var mapId = client.Tamer.Location.MapId;
                        var destination = client.Tamer.Location;

                        if (TargetSummon.DungeonMap)
                            _dungeonServer.RemoveClient(TargetSummon);
                        else if (TargetSummon.EventMap)
                            _eventServer.RemoveClient(TargetSummon);
                        else if (TargetSummon.PvpMap)
                            _pvpServer.RemoveClient(TargetSummon);
                        else
                            _mapServer.RemoveClient(TargetSummon);

                        TargetSummon.Tamer.NewLocation(mapId, destination.X, destination.Y);
                        await _sender.Send(new UpdateCharacterLocationCommand(TargetSummon.Tamer.Location));

                        TargetSummon.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                        await _sender.Send(new UpdateDigimonLocationCommand(TargetSummon.Tamer.Partner.Location));

                        TargetSummon.Tamer.UpdateState(CharacterStateEnum.Loading);
                        await _sender.Send(
                            new UpdateCharacterStateCommand(TargetSummon.TamerId, CharacterStateEnum.Loading));

                        TargetSummon.SetGameQuit(false);

                        TargetSummon.Send(new MapSwapPacket(_configuration[GamerServerPublic],
                            _configuration[GameServerPort],
                            client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));

                        var party = _partyManager.FindParty(TargetSummon.TamerId);

                        if (party != null)
                        {
                            party.UpdateMember(party[TargetSummon.TamerId], TargetSummon.Tamer);

                            _mapServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer).Serialize());

                            _dungeonServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer).Serialize());

                            _eventServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer).Serialize());

                            _pvpServer.BroadcastForTargetTamers(party.GetMembersIdList(),
                                new PartyMemberWarpGatePacket(party[TargetSummon.TamerId], client.Tamer).Serialize());
                        }

                        client.Send(new SystemMessagePacket($"You teleported Tamer: {TargetSummon.Tamer.Name}"));
                        TargetSummon.Send(new SystemMessagePacket($"You have been teleported by GM: {client.Tamer.Name}"));
                    }
                    break;

                case "exit":
                    {
                        var regex = @"^exit\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(
                                new SystemMessagePacket(
                                    $"Unknown command. Check the available commands on the Admin Portal."));
                            break;
                        }

                        if (client.Tamer.Location.MapId == 89)
                        {
                            var mapId = 3;

                            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(mapId));
                            var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));

                            if (client.DungeonMap)
                                _dungeonServer.RemoveClient(client);
                            else
                                _mapServer.RemoveClient(client);

                            var destination = waypoints.Regions.First();

                            client.Tamer.NewLocation(mapId, destination.X, destination.Y);
                            await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                            client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                            await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                            client.Tamer.UpdateState(CharacterStateEnum.Loading);
                            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                            client.SetGameQuit(false);

                            client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                                client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y).Serialize());
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"Este comando so pode ser usado no Mapa de Evento !!"));
                            break;
                        }
                    }
                    break;

                #endregion

                // -- BUFF ---------------------------------------

                #region Buff

                case "buff":
                    {
                        var regex = @"buff\s(add|remove)\s\d+";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !buff (add/remove)"));
                            break;
                        }

                        if (command.Length < 3)
                        {
                            client.Send(
                                new SystemMessagePacket("Invalid command format.\nType !buff (add/remove) (buffID)"));
                            break;
                        }

                        if (!int.TryParse(command[2], out var buffId))
                        {
                            client.Send(new SystemMessagePacket("Invalid item ID format. Please provide a valid number."));
                            break;
                        }

                        var buff = _assets.BuffInfo.FirstOrDefault(x => x.BuffId == buffId);

                        if (buff != null)
                        {
                            var duration = 0;

                            if (command[1].ToLower() == "add")
                            {
                                // Verify if is Tamer Skill
                                if (buff.SkillCode > 0)
                                {
                                    if (client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buff.BuffId))
                                    {
                                        client.Send(new SystemMessagePacket($"You already have this buff !!"));
                                        break;
                                    }

                                    var newCharacterBuff =
                                        CharacterBuffModel.Create(buff.BuffId, buff.SkillId, 0, (int)duration);
                                    newCharacterBuff.SetBuffInfo(buff);

                                    client.Tamer.BuffList.Buffs.Add(newCharacterBuff);

                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                    client.Send(new AddBuffPacket(client.Tamer.GeneralHandler, buff, (short)0, duration)
                                        .Serialize());

                                    await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                                }

                                // Verify if is Digimon Skill
                                if (buff.DigimonSkillCode > 0)
                                {
                                    if (client.Partner.BuffList.Buffs.Any(x => x.BuffId == buff.BuffId))
                                    {
                                        client.Send(new SystemMessagePacket($"Your Digimon already have this buff !!"));
                                        break;
                                    }

                                    var newDigimonBuff =
                                        DigimonBuffModel.Create(buff.BuffId, buff.SkillId, 0, (int)duration);
                                    newDigimonBuff.SetBuffInfo(buff);

                                    client.Partner.BuffList.Buffs.Add(newDigimonBuff);

                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                    client.Send(new AddBuffPacket(client.Partner.GeneralHandler, buff, (short)0, duration)
                                        .Serialize());

                                    await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
                                }

                                client.Send(new SystemMessagePacket($"New buff added"));
                            }
                            else if (command[1].ToLower() == "remove")
                            {
                                // Verify if is Tamer Skill
                                if (buff.SkillCode > 0)
                                {
                                    var characterBuff =
                                        client.Tamer.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == buff.BuffId);

                                    if (characterBuff == null)
                                    {
                                        client.Send(new SystemMessagePacket($"CharacterBuff not found"));
                                        break;
                                    }

                                    client.Tamer.BuffList.Buffs.Remove(characterBuff);

                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                    client.Send(new RemoveBuffPacket(client.Tamer.GeneralHandler, buff.BuffId).Serialize());

                                    await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                                }

                                // Verify if is Digimon Skill
                                if (buff.DigimonSkillCode > 0)
                                {
                                    var digimonBuff =
                                        client.Partner.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == buff.BuffId);

                                    if (digimonBuff == null)
                                    {
                                        client.Send(new SystemMessagePacket($"DigimonBuff not found"));
                                        break;
                                    }

                                    client.Partner.BuffList.Buffs.Remove(digimonBuff);

                                    client.Send(new UpdateStatusPacket(client.Tamer));
                                    client.Send(
                                        new RemoveBuffPacket(client.Partner.GeneralHandler, buff.BuffId).Serialize());

                                    await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
                                }

                                client.Send(new SystemMessagePacket($"Buff removed !!"));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket($"Unknown command.\nType !buff (add/remove) (buffId)"));
                                break;
                            }
                        }
                        else
                        {
                            client.Send(new SystemMessagePacket($"Buff not found !!"));
                        }
                    }
                    break;

                case "title":
                    {
                        var regex = @"title\s(add|remove)\s\d+";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !title (add/remove)"));
                            break;
                        }

                        if (command.Length < 3)
                        {
                            client.Send(
                                new SystemMessagePacket("Invalid command format.\nType !title (add/remove) (titleId)"));
                            break;
                        }

                        if (!short.TryParse(command[2], out var titleId))
                        {
                            client.Send(new SystemMessagePacket("Invalid item ID format. Please provide a valid number."));
                            break;
                        }

                        if (command[1].ToLower() == "add")
                        {
                            var newTitle =
                                _assets.AchievementAssets.FirstOrDefault(x => x.QuestId == titleId && x.BuffId > 0);

                            if (newTitle != null)
                            {
                                var buff = _assets.BuffInfo.FirstOrDefault(x => x.BuffId == newTitle.BuffId);

                                var duration = UtilitiesFunctions.RemainingTimeSeconds(0);

                                var newDigimonBuff = DigimonBuffModel.Create(buff.BuffId, buff.SkillId);

                                newDigimonBuff.SetBuffInfo(buff);

                                foreach (var partner in client.Tamer.Digimons.Where(x => x.Id != client.Tamer.Partner.Id))
                                {
                                    var partnernewDigimonBuff = DigimonBuffModel.Create(buff.BuffId, buff.SkillId);

                                    partnernewDigimonBuff.SetBuffInfo(buff);

                                    partner.BuffList.Add(partnernewDigimonBuff);

                                    await _sender.Send(new UpdateDigimonBuffListCommand(partner.BuffList));
                                }

                                client.Partner.BuffList.Add(newDigimonBuff);

                                var mapClient = _mapServer.FindClientByTamerId(client.TamerId);

                                if (mapClient == null)
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new AddBuffPacket(client.Partner.GeneralHandler, buff, (short)0, 0).Serialize());
                                else
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new AddBuffPacket(client.Partner.GeneralHandler, buff, (short)0, 0).Serialize());

                                client.Tamer.UpdateCurrentTitle(titleId);

                                if (mapClient == null)
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateCurrentTitlePacket(client.Tamer.AppearenceHandler, titleId).Serialize());
                                else
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateCurrentTitlePacket(client.Tamer.AppearenceHandler, titleId).Serialize());

                                client.Send(new UpdateStatusPacket(client.Tamer));

                                await _sender.Send(new UpdateCharacterTitleCommand(client.TamerId, titleId));
                                await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket($"Title {titleId} not found !!"));
                                break;
                            }
                        }
                        else if (command[1].ToLower() == "remove")
                        {
                            client.Send(new SystemMessagePacket($"Remove not implemented, sorry :)"));
                            break;
                        }
                    }
                    break;

                #endregion

                // -- PARTY --------------------------------------

                #region Party

                case "party":
                    {
                        //var regex = @"^party\s*$";
                        var regex = @"(party\slist$){1}|(party\sinfo$){1}";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !party"));
                            break;
                        }

                        if (command[1].ToLower() == "list")
                        {
                            var party = _partyManager.FindParty(client.TamerId);

                            if (party == null)
                            {
                                client.Send(new SystemMessagePacket($"The target tamer is not in a party."));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket($"Packet: Party Member List Sended !!"));
                                client.Send(new PartyMemberListPacket(party, client.TamerId,
                                    (byte)(party.Members.Count - 1)));
                            }
                        }
                        else if (command[1].ToLower() == "info")
                        {
                            var party = _partyManager.FindParty(client.TamerId);

                            if (party == null)
                            {
                                client.Send(new SystemMessagePacket($"The target tamer is not in a party."));
                            }
                            else
                            {
                                client.Send(new SystemMessagePacket($"Packet: Party Member Info Sended !!"));
                                client.Send(new PartyMemberInfoPacket(party[client.TamerId]).Serialize());
                            }
                        }
                    }
                    break;

                case "partymove":
                    {
                        var regex = @"^partymove\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !party"));
                            break;
                        }

                        var party = _partyManager.FindParty(client.TamerId);

                        if (party == null)
                        {
                            client.Send(new SystemMessagePacket($"The target tamer is not in a party."));
                            _logger.Warning($"The target tamer  {client.Tamer.Name} is not in a party.");
                        }
                        else
                        {
                            foreach (var target in party.Members.Values.Where(x => x.Id != client.TamerId))
                            {
                                var targetClient = _mapServer.FindClientByTamerId(target.Id);

                                if (targetClient == null) targetClient = _dungeonServer.FindClientByTamerId(target.Id);

                                if (targetClient == null) continue;

                                client.Send(new SystemMessagePacket($"Updating your position for tamer {target.Name}."));

                                targetClient.Send(
                                    UtilitiesFunctions.GroupPackets(
                                        new PartyMemberWarpGatePacket(party[client.TamerId], targetClient.Tamer)
                                            .Serialize(),
                                        new PartyMemberMovimentationPacket(party[client.TamerId]).Serialize()
                                    )
                                );
                            }
                        }
                    }
                    break;

                #endregion

                // -- Assets ----------------------------------------

                #region Reload Assets

                case "assetreload":
                    {
                        var regex = @"^assetreload\s*$";
                        var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

                        if (!match.Success)
                        {
                            client.Send(new SystemMessagePacket($"Unknown command.\nType !assetreload"));
                            break;
                        }

                        var assetsLoader = _assets.Reload();
                    }
                    break;

                #endregion

                // -- HELP ---------------------------------------

                #region Help

                case "oldhelp":
                    {
                        var commandsList = new List<string>
                        {
                            "hatch",
                            "tamer",
                            "digimon",
                            "currency",
                            "reload",
                            "dc",
                            "ban",
                            "item",
                            "gfstorage",
                            "cashstorage",
                            "hide",
                            "show",
                            "inv",
                            "storage",
                            "godmode",
                            "unlockevos",
                            "openseals",
                            "summon",
                            "heal",
                            "stats",
                            "tools",
                            "fullacc",
                            "evopack",
                            "spacepack",
                            "clon",
                            "maptamers",
                            "updatestats",
                            "live",
                            "maintenance",
                            "notice",
                            "ann",
                            "where",
                            "tp",
                            "tpto",
                            "tptamer",
                            "exit",
                            "buff",
                            "title",
                            "party",
                            "partymove",
                            "assetreload",
                            "delete",
                        };

                        var packetsToSend = new List<SystemMessagePacket>
                        { new SystemMessagePacket($"SYSTEM COMMANDS:", ""), };

                        int count = 0;

                        foreach (var chunk in commandsList.Chunk(10))
                        {
                            string commandsString = "";
                            chunk.ToList().ForEach(x =>
                            {
                                count++;
                                var space = count > 9 ? "   " : "    ";
                                var name = $"{count}.{space}!{x}";
                                if (x != chunk.Last())
                                {
                                    name += "\n";
                                }

                                commandsString += name;
                            });
                            packetsToSend.Add(new SystemMessagePacket(commandsString, ""));
                        }

                        // Convert packetsToSend to serialized form
                        var serializedPackets = packetsToSend.Select(x => x.Serialize()).ToArray();
                        client.Send(
                            UtilitiesFunctions.GroupPackets(
                                serializedPackets
                            ));
                    }
                    break;

                #endregion

                // -----------------------------------------------

                default:
                    client.Send(new SystemMessagePacket($"Unknown command.\nCheck the available commands typing !help"));
                    break;
            }
        }

        // ----------------------------------------------------------------------

        #region Commands

        private async Task ClearCommand(GameClient client, string[] command)
        {
            var regex = @"^clear\s+(inv|cash|gift)$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket("Unknown command.\nType !clear (inv | cash | gift)\n"));
                return;
            }

            if (command[1] == "inv")
            {
                client.Tamer.Inventory.Clear();
                client.Send(new SystemMessagePacket($" Inventory slots cleaned."));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            }
            else if (command[1] == "cash")
            {
                client.Tamer.AccountCashWarehouse.Clear();

                client.Send(new SystemMessagePacket($" CashWarehouse slots cleaned !!"));
                client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));

                await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));
            }
            else if (command[1] == "gift")
            {
                client.Tamer.GiftWarehouse.Clear();

                client.Send(new SystemMessagePacket($" GiftStorage slots cleaned."));
                client.Send(new LoadGiftStoragePacket(client.Tamer.GiftWarehouse));

                await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
            }
        }

        private async Task BattleLogCommand(GameClient client, string[] command)
        {
            var regex = @"^battlelog\s+(on|off)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command. Type !battlelog (on/off)."));
                return;
            }

            string action = match.Groups[1].Value;

            switch (action)
            {
                case "on":
                    if (!AttackManager.IsBattle)
                    {
                        AttackManager.SetBattleStatus(true);
                        client.Send(new NoticeMessagePacket($"Battle log is now active!"));
                    }
                    else
                    {
                        client.Send(new NoticeMessagePacket($"Battle log is already active..."));
                    }
                    break;

                case "off":
                    if (AttackManager.IsBattle)
                    {
                        AttackManager.SetBattleStatus(false);
                        client.Send(new NoticeMessagePacket($"Battle log is now inactive!"));
                    }
                    else
                    {
                        client.Send(new NoticeMessagePacket($"Battle log is already inactive..."));
                    }
                    break;

                default:
                    client.Send(new SystemMessagePacket($"Invalid command. Use !battlelog (on/off)"));
                    break;
            }
        }

        private async Task PlayersCommand(GameClient client, string[] command)
        {
            var regex = @"^players\s+(count)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (command.Length == 1)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !players count"));
                return;
            }
            else if (command.Length > 2)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !players count"));
                return;
            }

            switch (command[1])
            {
                case "count":
                    {
                        client.Send(new SystemMessagePacket($"Tamers Online: {client.Server.Clients.Count}"));
                        break;
                    }
            }
        }

        private async Task StatsCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^stats\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !stats"));
                return;
            }

            client.Send(new SystemMessagePacket($"Critical Damage: {client.Tamer.Partner.CD / 100}%\n" +
                $"Attribute Damage: {client.Tamer.Partner.ATT / 100}%\n" +
                $"Digimon SKD: {client.Tamer.Partner.SKD}\n" +
                $"Digimon SCD: {client.Tamer.Partner.SCD / 100}%\n" +
                $"Tamer BonusEXP: {client.Tamer.BonusEXP}%\n" +
                $"Tamer Move Speed: {client.Tamer.MS}"));
        }

        private async Task TimeCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^time\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !time"));
                return;
            }

            client.Send(new SystemMessagePacket($"Server Time is: {DateTime.UtcNow}"));
        }

        private async Task ItemCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"(item\s\d{1,7}\s\d{1,4}$){1}|(item\s\d{1,7}$){1}";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Invalid command!! Type !item (itemId) (amount)"));
                return;
            }

            var itemId = int.Parse(command[1].ToLower());

            var newItem = new ItemModel();
            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = command.Length == 2 ? 1 : int.Parse(command[2]);

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            var itemClone = (ItemModel)newItem.Clone();

            if (client.Tamer.Inventory.AddItem(newItem))
            {
                client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Send(new SystemMessagePacket($"ItemID {newItem.ItemId} x{newItem.Amount} added to your Inventory."));
            }
            else
            {
                client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
            }
        }

        private async Task ItemToCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^itemto\s+(\w+)\s+(\d{1,7})\s+(\d{1,4})$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Invalid command!! Type !itemto (TamerName) (itemId) (amount)"));
                return;
            }

            var tamerName = match.Groups[1].Value;
            GameClient? TargetPlayer = null;

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    TargetPlayer = _dungeonServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Pvp:
                    TargetPlayer = _pvpServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Event:
                    TargetPlayer = _eventServer.FindClientByTamerName(tamerName);
                    break;
                default:
                    TargetPlayer = _mapServer.FindClientByTamerName(tamerName);
                    break;
            }

            if (TargetPlayer == null)
            {
                client.Send(new SystemMessagePacket($"Tamer {tamerName} is not online!"));
                return;
            }

            var itemId = int.Parse(match.Groups[2].Value);
            var newItem = new ItemModel();

            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = match.Groups.Count == 4 ? int.Parse(match.Groups[3].Value) : 1;

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            var itemClone = (ItemModel)newItem.Clone();

            if (TargetPlayer.Tamer.Inventory.AddItem(newItem))
            {
                TargetPlayer.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                await _sender.Send(new UpdateItemsCommand(TargetPlayer.Tamer.Inventory));

                _logger.Information($"Tamer {tamerName} received the item {itemId} in their inventory.");
                TargetPlayer.Send(new SystemMessagePacket($"Tamer {client.Tamer.Name} sended the item {newItem.ItemInfo.Name} to you in inventory."));
                client.Send(new SystemMessagePacket($"Tamer {tamerName} received the item {itemId} in their inventory."));
            }
            else
            {
                client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                client.Send(new SystemMessagePacket($"Tamer {tamerName}'s inventory is full."));
                _logger.Error($"Tamer {tamerName}'s inventory is full.");
            }
        }

        private async Task BitsCommand(GameClient client, string[] command)
        {
            var regex = @"^bits\s+(add|remove)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (command.Length < 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !bits (add/remove) amount"));
                return;
            }
            else if (command.Length > 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !bits (add/remove) amount"));
                return;
            }

            switch (command[1])
            {
                case "add":
                    {
                        var value = long.Parse(command[2]);

                        client.Tamer.Inventory.AddBits(value);

                        client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());

                        await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory.Id, client.Tamer.Inventory.Bits));
                    }
                    break;

                case "remove":
                    {
                        var value = long.Parse(command[2]);

                        client.Tamer.Inventory.RemoveBits(value);

                        client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize());

                        await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory.Id, client.Tamer.Inventory.Bits));
                    }
                    break;
            }
        }

        private async Task CrownCommand(GameClient client, string[] command)
        {
            var regex = @"^crown\s+(add|remove)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (command.Length < 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !crown (add/remove) amount"));
                return;
            }
            else if (command.Length > 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !crown (add/remove) amount"));
                return;
            }

            switch (command[1])
            {
                case "add":
                    {
                        var value = int.Parse(command[2]);

                        client.AddPremium(value);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));
                    }
                    break;

                case "remove":
                    {
                        var value = int.Parse(command[2]);

                        client.RemovePremium(value);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));
                    }
                    break;
            }
        }

        private async Task CrownToCommand(GameClient client, string[] command)
        {
            var regex = @"^crownto\s+([A-Za-z0-9\s]+)\s+(add|remove)\s+(\d+)$";
            var commandString = string.Join(" ", command);
            var match = Regex.Match(commandString, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !crownto (TamerName) (add/remove) amount"));
                return;
            }

            var tamerName = match.Groups[1].Value;
            var action = match.Groups[2].Value;
            var amount = int.Parse(match.Groups[3].Value);

            GameClient? TargetPlayer = null;

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    TargetPlayer = _dungeonServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Pvp:
                    TargetPlayer = _pvpServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Event:
                    TargetPlayer = _eventServer.FindClientByTamerName(tamerName);
                    break;
                default:
                    TargetPlayer = _mapServer.FindClientByTamerName(tamerName);
                    break;
            }

            if (TargetPlayer == null)
            {
                client.Send(new SystemMessagePacket($"Tamer {tamerName} not found."));
                return;
            }

            switch (action)
            {
                case "add":
                    {
                        TargetPlayer.AddPremium(amount);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(TargetPlayer.Premium, TargetPlayer.Silk, TargetPlayer.AccountId));

                        client.Send(new SystemMessagePacket($"Tamer {tamerName} received {amount} crowns."));
                        TargetPlayer.Send(new SystemMessagePacket($"Tamer {client.Tamer.Name} sent {amount} crowns to you."));
                    }
                    break;

                case "remove":
                    {
                        TargetPlayer.RemovePremium(amount);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(TargetPlayer.Premium, TargetPlayer.Silk, TargetPlayer.AccountId));

                        client.Send(new SystemMessagePacket($"Tamer {tamerName} had {amount} crowns removed."));
                        TargetPlayer.Send(new SystemMessagePacket($"Tamer {client.Tamer.Name} removed {amount} crowns from you."));
                    }
                    break;
            }
        }

        private async Task SilkCommand(GameClient client, string[] command)
        {
            var regex = @"^silk\s+(add|remove)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (command.Length < 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !silk (add/remove) amount"));
                return;
            }
            else if (command.Length > 3)
            {
                client.Send(new SystemMessagePacket($"Invalid Command!! Type !silk (add/remove) amount"));
                return;
            }

            switch (command[1])
            {
                case "add":
                    {
                        var value = int.Parse(command[2]);

                        client.AddSilk(value);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));
                    }
                    break;

                case "remove":
                    {
                        var value = int.Parse(command[2]);

                        client.RemoveSilk(value);

                        await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));
                    }
                    break;
            }
        }

        private async Task BanCommand(GameClient client, string[] command)
        {
            if (command.Length < 4)
            {
                client.Send(new SystemMessagePacket($"Incorret command, use !ban (TamerName) (Hours) (BanReason)"));
                return;
            }

            string tamerName = command[1];
            string TimeByHours = command[2];
            string banReason = string.Join(" ", command.Skip(3));

            GameClient? TargetPlayer = null;

            var TargetBan = await _sender.Send(new CharacterByNameQuery(tamerName));

            if (TargetBan == null)
            {
                _logger.Warning($"Character not found with name {tamerName}.");
                client.Send(new SystemMessagePacket($"Tamer not found with name {tamerName}."));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    TargetPlayer = _dungeonServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Pvp:
                    TargetPlayer = _pvpServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Event:
                    TargetPlayer = _eventServer.FindClientByTamerName(tamerName);
                    break;
                default:
                    TargetPlayer = _mapServer.FindClientByTamerName(tamerName);
                    break;
            }

            if (!int.TryParse(TimeByHours, out int timeByHours))
            {
                _logger.Warning($"Invalid hours format: {TimeByHours}");
                client.Send(new SystemMessagePacket($"Invalid hours format. Use an number."));
                return;
            }

            var startDate = DateTime.Now;
            var endDate = startDate.AddHours(timeByHours);

            var chatPacket = new NoticeMessagePacket($"Tamer {tamerName} has been BANNED !!").Serialize();
            client.SendToAll(chatPacket);

            await _sender.Send(new AddAccountBlockCommand(TargetBan.AccountId, AccountBlockEnum.Permanent, banReason, startDate, endDate));

            if (TargetPlayer != null)
            {
                TimeSpan timeRemaining = endDate - startDate;

                double totalSeconds = timeRemaining.TotalSeconds;

                if (totalSeconds >= 0 && totalSeconds <= uint.MaxValue)
                {
                    uint secondsRemaining = (uint)totalSeconds;

                    TargetPlayer.Send(new BanUserPacket(secondsRemaining, banReason));
                }
                else
                {
                    _logger.Error($"Invalid ban duration: {totalSeconds} seconds.");
                }
            }
        }

        private async Task PvpCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"pvp\s+(on|off)";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !pvp (on/off)"));
                return;
            }

            if (client.Tamer.InBattle)
            {
                client.Send(new SystemMessagePacket($"You can't turn off pvp on battle !"));
                return;
            }

            string action = match.Groups[1].Value;

            switch (action)
            {
                case "on":
                    {
                        if (client.Tamer.PvpMap == false)
                        {
                            client.Tamer.PvpMap = true;
                            client.Send(new NoticeMessagePacket($"PVP turned on !!"));
                        }
                        else
                        {
                            client.Send(new NoticeMessagePacket($"PVP is already on ..."));
                        }
                    }
                    break;

                case "off":
                    {
                        if (client.Tamer.PvpMap == true)
                        {
                            client.Tamer.PvpMap = false;
                            client.Send(new NoticeMessagePacket($"PVP turned off !!"));
                        }
                        else
                        {
                            client.Send(new NoticeMessagePacket($"PVP is already off ..."));
                        }
                    }
                    break;
            }
        }

        private async Task HatchCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^hatch";
            var match = Regex.IsMatch(message, regex, RegexOptions.IgnoreCase);

            if (!match)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !hatch (Type) (Name)"));
                return;
            }

            if (command.Length < 3)
            {
                client.Send(new SystemMessagePacket("Invalid command.\nType !hatch (Type) (Name)"));
                return;
            }

            if (!int.TryParse(command[1], out int digiId))
            {
                client.Send(new SystemMessagePacket("Invalid DigimonId.\nType numeric value."));
                return;
            }

            var digiName = command[2];

            if (digiId == 31001 || digiId == 31002 || digiId == 31003 || digiId == 31004)
            {
                client.Send(new SystemMessagePacket($"You cant hatch starter digimon, sorry :P"));
                return;
            }

            var digiBase = _assets.DigimonBaseInfo.First(x => x.Type == digiId);

            if (digiBase == null)
            {
                client.Send(new SystemMessagePacket($"Digimon Type {digiId} not found on database !!"));
                _logger.Error($"Digimon Type {digiId} not found on DigimonBaseInfo !! [ Hatch Command ]");
                return;
            }

            try
            {
                var digiEvo = _assets.EvolutionInfo.First(x => x.Type == digiId);

                if (digiEvo == null)
                {
                    client.Send(new SystemMessagePacket($"Digimon Type {digiId} not available,\nneed to be Rookie/Spirit !!"));
                    return;
                }
            }
            catch (Exception ex)
            {
                client.Send(new SystemMessagePacket($"Digimon Type {digiId} not available,\nneed to be Rookie/Spirit !!"));
                return;
            }

            byte digimonSlot = (byte)Enumerable.Range(0, client.Tamer.DigimonSlots)
                .FirstOrDefault(slot => client.Tamer.Digimons.FirstOrDefault(x => x.Slot == slot) == null);

            var newDigimon = DigimonModel.Create(digiName, digiId, digiId, DigimonHatchGradeEnum.Perfect, 12500, digimonSlot);

            newDigimon.NewLocation(client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y);

            newDigimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(newDigimon.BaseType));
            newDigimon.SetBaseStatus(_statusManager.GetDigimonBaseStatus(newDigimon.BaseType, newDigimon.Level, newDigimon.Size));

            var digimonEvolutionInfo = _assets.EvolutionInfo.First(x => x.Type == newDigimon.BaseType);

            newDigimon.AddEvolutions(digimonEvolutionInfo);

            if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null || !newDigimon.Evolutions.Any())
            {
                _logger.Error($"Unknown digimon info for {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                return;
            }

            newDigimon.SetTamer(client.Tamer);

            client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000), newDigimon.Slot));

            var digimonInfo = _mapper.Map<DigimonModel>(await _sender.Send(new CreateDigimonCommand(newDigimon)));

            client.Tamer.AddDigimon(digimonInfo);

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

            client.Send(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());

            // ------------------------------------------------------------------------------------------------------

            var digimonBaseInfo = newDigimon.BaseInfo;
            var digimonEvolutions = newDigimon.Evolutions;

            //_logger.Information($"DigimonType: {newDigimon.BaseType} | DigimonInfo: {digimonEvolutionInfo?.Id.ToString()}");

            var encyclopediaExists =
                client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo?.Id);

            // Check if encyclopedia exists
            if (!encyclopediaExists && digimonEvolutionInfo != null)
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

                    var encyclopediaEvo = CharacterEncyclopediaEvolutionsModel.Create(x.Type, slotLevel, Convert.ToBoolean(x.Unlocked));

                    _logger.Debug(
                        $"{encyclopediaEvo.Id}, {encyclopediaEvo.DigimonBaseType}, {encyclopediaEvo.SlotLevel}, {encyclopediaEvo.IsUnlocked}");

                    encyclopedia.Evolutions.Add(encyclopediaEvo);
                });

                var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));

                client.Tamer.Encyclopedia.Add(encyclopediaAdded);
            }
        }

        private async Task GodmodeCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"godmode\s+(on|off)";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !godmode (on/off)"));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            string action = match.Groups[1].Value;

            switch (action)
            {
                case "on":
                    {
                        switch (mapConfig!.Type)
                        {
                            case MapTypeEnum.Pvp:
                                {
                                    client.Tamer.SetGodMode(false);
                                }
                                break;

                            default:
                                {
                                    if (client.Tamer.GodMode)
                                    {
                                        client.Send(new SystemMessagePacket($"You are already in god mode."));
                                    }
                                    else
                                    {
                                        client.Tamer.SetGodMode(true);

                                        client.Send(new SystemMessagePacket($"God mode enabled !!"));
                                    }
                                }
                                break;
                        }
                    }
                    break;

                case "off":
                    {
                        if (!client.Tamer.GodMode)
                        {
                            client.Send(new SystemMessagePacket($"You are already with god mode disabled."));
                        }
                        else
                        {
                            client.Tamer.SetGodMode(false);

                            client.Send(new SystemMessagePacket($"God mode disabled !!"));
                        }
                    }
                    break;
            }
        }

        private async Task BurnexpCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"burnexp\s+(on|off)";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !burnexp (on/off)"));
                return;
            }

            string action = match.Groups[1].Value;

            switch (action)
            {
                case "on":
                    {
                        var serverInfo = _mapper.Map<ServerObject>(await _sender.Send(new ServerByIdQuery(client.ServerId)));

                        client.SetServerExperienceType(2);
                        client.SetServerExperience(serverInfo.ExperienceBurn);

                        await _sender.Send(new UpdateServerCommand(serverInfo.Id, serverInfo.Name,
                            serverInfo.Experience, serverInfo.Maintenance, serverInfo.ExperienceBurn, 2));

                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new ServerExperiencePacket(serverInfo).Serialize());

                        client.Send(new NoticeMessagePacket($"Server Exp is now in Burn !!"));
                    }
                    break;

                case "off":
                    {
                        var serverInfo = _mapper.Map<ServerObject>(await _sender.Send(new ServerByIdQuery(client.ServerId)));

                        client.SetServerExperienceType(1);
                        client.SetServerExperience(serverInfo.Experience);

                        await _sender.Send(new UpdateServerCommand(serverInfo.Id, serverInfo.Name,
                            serverInfo.Experience, serverInfo.Maintenance, serverInfo.ExperienceBurn, 1));

                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new ServerExperiencePacket(serverInfo).Serialize());

                        client.Send(new NoticeMessagePacket($"Server Exp back to normal !!"));
                    }
                    break;
            }

        }

        private async Task MembershipCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"membership\s(add|remove)(\s\d{1,9})?$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !membership (add/remove) (days)"));
                return;
            }

            string action = match.Groups[1].Value;

            switch (action)
            {
                case "add":
                    {
                        var valueInDays = int.Parse(command[2]);

                        var value = valueInDays * 24 * 3600;

                        client.IncreaseMembershipDuration(value);

                        client.Send(new MembershipPacket(client.MembershipExpirationDate!.Value, client.MembershipUtcSeconds));

                        await _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, client.MembershipExpirationDate));

                        var buff = _assets.BuffInfo.Where(x => x.BuffId == 50121 || x.BuffId == 50122 || x.BuffId == 50123).ToList();

                        int duration = client.MembershipUtcSecondsBuff;

                        buff.ForEach(buffAsset =>
                        {
                            if (!client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buffAsset.BuffId))
                            {
                                var newCharacterBuff = CharacterBuffModel.Create(buffAsset.BuffId, buffAsset.SkillId, 2592000, duration);

                                newCharacterBuff.SetBuffInfo(buffAsset);

                                client.Tamer.BuffList.Buffs.Add(newCharacterBuff);

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buffAsset, 0, duration).Serialize());
                            }
                            else
                            {
                                var buffData = client.Tamer.BuffList.Buffs.First(x => x.BuffId == buffAsset.BuffId);

                                if (buffData != null)
                                {
                                    buffData.SetDuration(duration, true);

                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                        new UpdateBuffPacket(client.Tamer.GeneralHandler, buffAsset, 0, duration).Serialize());
                                }
                            }
                        });

                        await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));

                        client.Send(new UpdateStatusPacket(client.Tamer));
                    }
                    break;

                case "remove":
                    {
                        client.RemoveMembership();

                        int duration = client.MembershipUtcSecondsBuff;

                        client.Send(new MembershipPacket());

                        await _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, client.MembershipExpirationDate));

                        var secondsUTC = (client.MembershipExpirationDate.Value - DateTime.UtcNow).TotalSeconds;

                        if (secondsUTC <= 0)
                        {
                            var buff = _assets.BuffInfo.Where(x => x.BuffId == 50121 || x.BuffId == 50122 || x.BuffId == 50123).ToList();

                            buff.ForEach(buffAsset =>
                            {
                                if (client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buffAsset.BuffId))
                                {
                                    var characterBuff = client.Tamer.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == buffAsset.BuffId);

                                    client.Tamer.BuffList.Buffs.Remove(characterBuff!);

                                    client.Send(new RemoveBuffPacket(client.Tamer.GeneralHandler, buffAsset.BuffId).Serialize());
                                }
                            });

                            await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));
                    }
                    break;

                default:
                    client.Send(new SystemMessagePacket($"Unknown command. Check the available commands on the Admin Portal."));
                    break;
            }
        }

        private async Task CashItemCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"(cashitem\s\d{1,7}\s\d{1,4}$){1}|(cashitem\s\d{1,7}$){1}";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Invalid command!! Type !cashitem (itemId) (amount)"));
                return;
            }

            var itemId = int.Parse(command[1].ToLower());

            var newItem = new ItemModel();
            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = command.Length == 2 ? 1 : int.Parse(command[2]);

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            var itemClone = (ItemModel)newItem.Clone();

            try
            {
                if (client.Tamer.AccountCashWarehouse.AddItem(newItem))
                {
                    client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));

                    client.Send(new SystemMessagePacket($"ItemID {newItem.ItemId} x{newItem.Amount} added to your CashWarehouse."));
                }
                else
                {
                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[ERRO]::{ex.Message}");
            }
        }

        private async Task CashItemToCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^cashitemto\s+(\w+)\s+(\d{1,7})\s+(\d{1,4})$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Invalid command!! Type !cashitem (TamerName) (itemId) (amount)"));
                return;
            }

            var tamerName = match.Groups[1].Value;
            GameClient? TargetPlayer = null;

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    TargetPlayer = _dungeonServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Pvp:
                    TargetPlayer = _pvpServer.FindClientByTamerName(tamerName);
                    break;
                case MapTypeEnum.Event:
                    TargetPlayer = _eventServer.FindClientByTamerName(tamerName);
                    break;
                default:
                    TargetPlayer = _mapServer.FindClientByTamerName(tamerName);
                    break;
            }

            if (TargetPlayer == null)
            {
                client.Send(new SystemMessagePacket($"Tamer {tamerName} is not online!"));
                return;
            }

            var itemId = int.Parse(match.Groups[2].Value);
            var newItem = new ItemModel();

            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                _logger.Warning($"No item info found with ID {itemId} for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = match.Groups.Count == 4 ? int.Parse(match.Groups[3].Value) : 1;

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            var itemClone = (ItemModel)newItem.Clone();

            if (TargetPlayer.Tamer.AccountCashWarehouse.AddItem(newItem))
            {
                TargetPlayer.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));

                TargetPlayer.Send(new SystemMessagePacket($"Tamer {client.Tamer.Name} sended the item {newItem.ItemInfo.Name} to your CashWarehouse."));
                client.Send(new SystemMessagePacket($"Tamer {tamerName} received the item {itemId} in CashWarehouse."));
            }
            else
            {
                client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                client.Send(new SystemMessagePacket($"Tamer {tamerName}'s CashWarehouse is full."));
            }
        }

        private async Task ReloadCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^reload\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !reload"));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            client.Tamer.UpdateState(CharacterStateEnum.Loading);

            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.RemoveClient(client);
                    break;
                default:
                    _mapServer.RemoveClient(client);
                    break;
            }

            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();

            client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort],
                client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));
        }

        private async Task HelpCommand(GameClient client, string[] command)
        {
            int pageNumber = 1;

            if (command.Length > 2)
            {
                client.Send(new SystemMessagePacket("Invalid Command ... Type !help (page)"));
                return;
            }

            if (command.Length == 2)
            {
                if (int.TryParse(command[1], out pageNumber))
                {
                    if (pageNumber < 1)
                    {
                        client.Send(new SystemMessagePacket("Page number invalid !!"));
                        return;
                    }
                }
            }

            string helpMessage = GetHelpPage(pageNumber);
            client.Send(new SystemMessagePacket(helpMessage, ""));
        }

        private async Task PingCommand(GameClient client, string[] command)
        {
            if (command.Length > 1)
            {
                client.Send(new SystemMessagePacket($"Invalid Command ... Type !ping"));
                return;
            }

            var target = _configuration[GamerServerPublic];

            Ping pingSender = new Ping();
            PingReply reply = null;

            try
            {
                reply = await pingSender.SendPingAsync(target);

                if (reply.Status == IPStatus.Success)
                {
                    client.Send(new SystemMessagePacket($"Your Ping is {reply.RoundtripTime} ms"));
                }
                else
                {
                    client.Send(new SystemMessagePacket($"Failed to ping!! Status: {reply.Status}"));
                }
            }
            catch (PingException ex)
            {
                client.Send(new SystemMessagePacket($"Erro ao executar ping para o servidor ({target}): {ex.Message}"));
            }
            catch (Exception ex)
            {
                client.Send(new SystemMessagePacket($"Erro inesperado ao executar ping: {ex.Message}"));
            }
        }

        #endregion

        // ----------------------------------------------------------------------

        private async Task AddItemToInventory(GameClient client, int itemId, int amount)
        {
            var newItem = new ItemModel();

            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                client.Send(new SystemMessagePacket($"No item info found with ID {itemId}."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = amount <= 0 ? 1 : amount;

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            if (client.Tamer.Inventory.AddItem(newItem))
            {
                client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Send(new SystemMessagePacket($"ItemID {newItem.ItemId} x{newItem.Amount} added to your Inventory."));
            }
            else
            {
                client.Send(new SystemMessagePacket($"No space inside Inventory."));
            }
        }

        private async Task SummonMonster(GameClient client, SummonModel? SummonInfo)
        {
            foreach (var mobToAdd in SummonInfo.SummonedMobs)
            {
                var mob = (SummonMobModel)mobToAdd.Clone();

                int radius = 500;
                var random = new Random();

                int xOffset = random.Next(-radius, radius + 1);
                int yOffset = random.Next(-radius, radius + 1);

                int bossX = client.Tamer.Location.X + xOffset;
                int bossY = client.Tamer.Location.Y + yOffset;

                if (client.DungeonMap)
                {
                    var map = _dungeonServer.Maps.FirstOrDefault(
                        x => x.Clients.Exists(gameClient => gameClient.TamerId == client.TamerId));

                    var mobId = map.SummonMobs.Count + 1;

                    mob.SetId(mobId);

                    if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                    {
                        bossX = mob.Location.X;
                        bossY = mob.Location.Y;

                        mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    }
                    else
                    {
                        mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    }

                    mob.SetDuration();
                    mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);
                    _dungeonServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.TamerId);
                }
                else
                {
                    var map = _mapServer.Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == client.TamerId));

                    var mobId = map.SummonMobs.Count + 1;

                    mob.SetId(mobId);

                    if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                    {
                        bossX = mob.Location.X;
                        bossY = mob.Location.Y;

                        mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    }
                    else
                    {
                        mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    }

                    mob.SetDuration();
                    mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                    _mapServer.AddSummonMobs(client.Tamer.Location.MapId, mob);
                }
            }
        }

        private async Task NewSummon(GameClient client, SummonModel SummonInfo)
        {
            var count = 0;

            foreach (var mobToAdd in SummonInfo.SummonedMobs)
            {
                count++;

                var mob = (SummonMobModel)mobToAdd.Clone();

                mob.TamersViewing.Clear();

                int radius = 500; // Ajuste este valor para controlar a dispersão dos chefes
                var random = new Random();

                // Gerando valores aleatórios para deslocamento em X e Y
                int xOffset = random.Next(-radius, radius + 1);
                int yOffset = random.Next(-radius, radius + 1);

                // Calculando as novas coordenadas do chefe de raid
                int bossX = client.Tamer.Location.X + xOffset;
                int bossY = client.Tamer.Location.Y + yOffset;

                if (client.DungeonMap)
                {
                    var map = _dungeonServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.TamerId == client.TamerId));

                    if (map == null)
                    {
                        client.Send(new SystemMessagePacket($"DungeonMap not found !!"));
                        break;
                    }

                    var mobId = map.SummonMobs.Count + 1;

                    mob.SetId(mobId);
                    mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    mob.SetDuration();
                    mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                    _dungeonServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.TamerId);
                }
                else if (client.EventMap)
                {
                    var map = _eventServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

                    if (map == null)
                    {
                        client.Send(new SystemMessagePacket($"EventMap not found !!"));
                        break;
                    }

                    var mobId = map.SummonMobs.Count + 1;

                    mob.SetId(mobId);
                    mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                    mob.SetDuration();
                    mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                    _eventServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.TamerId);
                }
                else
                {
                    var map = _mapServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);

                    if (map == null)
                    {
                        client.Send(new SystemMessagePacket($"Map not found !!"));
                        break;
                    }
                    else
                    {
                        var mobId = map.SummonMobs.Count + 1;

                        mob.SetId(mobId);
                        mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        mob.SetDuration();
                        mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                        _mapServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.Tamer.Channel);
                    }
                }
            }

        }

        private string GetHelpPage(int pageNumber)
        {
            switch (pageNumber)
            {
                case 1:
                    return "Commands (Page 1 of 4):\n1. !clear\n2. !battlelog\n3. !players\n4. !stats\n5. !time\nType !help {page} for more pages.";
                case 2:
                    return "Commands (Page 2 of 4):\n6. !item\n7. !itemto\n8. !bits\n9. !bitsto\n10. !silk\nType !help {page} for more pages.";
                case 3:
                    return "Commands (Page 3 of 4):\n11. !crown\n12. !crownto\n13. !ban\n14. !pvp\n15. !hatch\nType !help {page} for more pages.";
                case 4:
                    return "Commands (Page 4 of 4):\n16. !godmode\n17. !burnexp\n18. !membership\n19. !reload\nType !help {page} for more pages.";
                //case 5:
                    //return "Commands (Page 5):\n21. !crown\n22. !crownto\n23. !ban\n24. !pvp\n25. !hatch\nType !help {page} for more pages.";
                default:
                    return "Page not found. Type !help (page) [1 to 4].";
            }
        }

        // ----------------------------------------------------------------------

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}