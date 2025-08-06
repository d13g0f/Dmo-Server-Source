using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Enums.Account;

namespace DigitalWorldOnline.Game.Commands
{
    public sealed class PlayerCommandsProcessor : IDisposable
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
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IConfiguration _configuration;

        private Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)> commands;

        public PlayerCommandsProcessor(PartyManager partyManager, StatusManager statusManager, ExpManager expManager, AssetsLoader assets,
            MapServer mapServer,DungeonsServer dungeonsServer,PvpServer pvpServer,
            ILogger logger, ISender sender, IConfiguration configuration)
        {
            _partyManager = partyManager;
            _expManager = expManager;
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _configuration = configuration;
            InitializeCommands();
        }

        // Comands and permissions
        private void InitializeCommands()
        {
            commands = new Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)>
            {
                { "clear", (ClearCommand, null) },
                { "battlelog", (BattleLogCommand , null) },
                { "stats", (StatsCommand, null) },
                { "time", (TimeCommand, null) },
                { "deckload", (DeckLoadCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Vip1, AccountAccessLevelEnum.Vip2, AccountAccessLevelEnum.Vip3 }) },
                { "pvp", (PvpCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Vip1, AccountAccessLevelEnum.Vip2, AccountAccessLevelEnum.Vip3 }) },
                { "help", (HelpCommand, null) }, // Help está disponível para todos
            };
        }

        public async Task ExecuteCommand(GameClient client, string message)
        {
            var command = Regex.Replace(message.Trim().ToLower(), @"\s+", " ").Split(' ');

            if (commands.TryGetValue(command[0], out var commandInfo))
            {
                if (commandInfo.AccessLevels?.Contains(client.AccessLevel) != false)
                {
                    //_logger.Information($"Sending Command!! [PlayerCommand]");
                    await commandInfo.Command(client, command);
                }
                else
                {
                    _logger.Warning($"Tamer {client.Tamer.Name} tryed to use the command {message} without permission !! [PlayerCommand]");
                    client.Send(new SystemMessagePacket("You do not have permission to use this command."));
                }
            }
            else
            {
                client.Send(new SystemMessagePacket($"Invalid Command !! Type !help"));
            }
        }

        #region Commands

        private async Task ClearCommand(GameClient client, string[] command)
        {
            var regex = @"^clear\s+(inv|cash|gift)$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket("Unknown command.\nType !clear {inv|cash|gift}\n"));
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
                client.Send(new SystemMessagePacket($" CashStorage slots cleaned."));
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

            string action = match.Groups[1].Value.ToLower();
            switch (action)
            {
                case "on":
                    if (!AttackManager.IsBattle)
                    {
                        AttackManager.StartBattle();
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
                        AttackManager.EndBattle();
                        client.Send(new NoticeMessagePacket($"Battle log is now inactive!"));
                    }
                    else
                    {
                        client.Send(new NoticeMessagePacket($"Battle log is already inactive..."));
                    }
                    break;
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

        private async Task DeckLoadCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^deckload\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !deckload"));
                return;
            }

            var evolution = client.Partner.Evolutions[0];

            _logger.Information(
                $"Evolution ID: {evolution.Id} | Evolution Type: {evolution.Type} | Evolution Unlocked: {evolution.Unlocked}");

            var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?.Lines
                .FirstOrDefault(x => x.Type == evolution.Type);

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
                }
            }

            // --- UNLOCK -------------------------------------------------------------------------------------------

            var encyclopedia =
                client.Tamer.Encyclopedia.First(x => x.DigimonEvolutionId == evoInfo.EvolutionId);

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
            }

            // ------------------------------------------------------------------------------------------------------

            client.Send(new SystemMessagePacket($"Encyclopedia verifyed and updated !!"));
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

            string action = match.Groups[1].Value.ToLower();

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

        // --- HELP ---------------------------------------------------------------
        private async Task HelpCommand(GameClient client, string[] command)
        {
            if (command.Length == 1)
            {
                client.Send(new SystemMessagePacket("Commands:\n1. !clear\n2. !stats\n3. !time\nType !help {command} for more details.", ""));
            }
            else if (command.Length == 2)
            {
                if (command[1] == "inv")
                {
                    client.Send(new SystemMessagePacket("Command !clear inv: Clear your inventory"));
                }
                else if (command[1] == "cash")
                {
                    client.Send(new SystemMessagePacket("Command !clear cash: Clear your CashStorage"));
                }
                else if (command[1] == "gift")
                {
                    client.Send(new SystemMessagePacket("Command !clear gift: Clear your GiftStorage"));
                }
                else if (command[1] == "stats")
                {
                    client.Send(new SystemMessagePacket("Command !stats: Show hidden stats"));
                }
                else if (command[1] == "time")
                {
                    client.Send(new SystemMessagePacket("Command !time: Shows the server time"));
                }
                else if (command[1] == "pvp")
                {
                    client.Send(new SystemMessagePacket("Command !pvp (on/off): Turn on/off pvp mode"));
                }
                else
                {
                    client.Send(new SystemMessagePacket("Invalid Command !! Type !help to see the commands.", ""));
                }
            }
            else
            {
                client.Send(new SystemMessagePacket("Invalid Command !! Type !help"));
            }
        }

        #endregion

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
