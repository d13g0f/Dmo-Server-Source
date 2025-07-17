using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using MediatR;
using GameServer.Logging;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerDigiclonePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerDigiclone;

        private readonly MapServer _mapServer;
        private readonly AssetsLoader _assets;
        private readonly DungeonsServer _dungeonServer;
        private readonly ConfigsLoader _configs;
        private readonly ISender _sender;

        public PartnerDigiclonePacketProcessor(
            MapServer mapServer,
            AssetsLoader assets,
            ConfigsLoader configs,
            ISender sender,
            DungeonsServer dungeonsServer)
        {
            _mapServer = mapServer;
            _assets = assets;
            _configs = configs;
            _sender = sender;
            _dungeonServer = dungeonsServer;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            // Log de inicio del procesamiento
            await GameLogger.LogInfo($"Inicio del procesamiento del paquete PartnerDigiclone para TamerId={client.TamerId}", "digiclone");

            var packet = new GamePacketReader(packetData);
            var cloneType = (DigicloneTypeEnum)packet.ReadInt();
            var digicloneSlot = packet.ReadInt();
            var backupSlot = packet.ReadInt();

            // Log de los datos recibidos
            await GameLogger.LogInfo($"Datos del paquete: CloneType={cloneType}, DigicloneSlot={digicloneSlot}, BackupSlot={backupSlot}", "digiclone");

            var digicloneItem = client.Tamer.Inventory.FindItemBySlot(digicloneSlot);
            if (digicloneItem == null)
            {
                await GameLogger.LogWarning($"Ítem de clonación inválido en el slot {digicloneSlot} para TamerId={client.TamerId}", "digiclone");
                client.Send(new SystemMessagePacket($"Invalid clone item at slot {digicloneSlot}."));
                return;
            }
            await GameLogger.LogInfo($"Ítem de clonación encontrado: ItemId={digicloneItem.ItemId}, Section={digicloneItem.ItemInfo.Section}", "digiclone");

            // Log del estado actual del Digiclone
            await GameLogger.LogInfo($"Estado del Digiclone: DigimonId={client.Partner.Digiclone?.DigimonId}, DigicloneId={client.Partner.Digiclone?.Id}, CloneType={cloneType}, CurrentLevel={client.Partner.Digiclone?.GetCurrentLevel(cloneType)}", "digiclone");

            var currentCloneLevel = client.Partner.Digiclone.GetCurrentLevel(cloneType);
            var cloneConfig = _configs.Clones.FirstOrDefault(x => x.Type == cloneType && x.Level == currentCloneLevel + 1);
            if (cloneConfig == null)
            {
                await GameLogger.LogWarning($"Configuración de clonación inválida para CloneType={cloneType}, Level={currentCloneLevel + 1}", "digiclone");
                client.Send(new SystemMessagePacket($"Invalid clone config."));
                return;
            }
            await GameLogger.LogInfo($"CloneConfig encontrado: Type={cloneConfig.Type}, Level={cloneConfig.Level}, SuccessChance={cloneConfig.SuccessChance}, BreakChance={cloneConfig.BreakChance}", "digiclone");

            var clonePriceAsset = _assets.Clones.FirstOrDefault(x => x.ItemSection == digicloneItem.ItemInfo.Section);
            if (clonePriceAsset == null)
            {
                await GameLogger.LogWarning($"Assets de precio de clonación inválidos para ItemSection={digicloneItem.ItemInfo.Section}", "digiclone");
                client.Send(UtilitiesFunctions.GroupPackets(
                    new SystemMessagePacket($"Invalid clone assets.").Serialize(),
                    new DigicloneResultPacket(DigicloneResultEnum.Fail, client.Partner.Digiclone).Serialize()
                ));
                return;
            }
            await GameLogger.LogInfo($"ClonePriceAsset encontrado: ItemSection={clonePriceAsset.ItemSection}, MinLevel={clonePriceAsset.MinLevel}, MaxLevel={clonePriceAsset.MaxLevel}, Bits={clonePriceAsset.Bits}", "digiclone");

           // Validación del ítem de clonación y niveles
        if (!(cloneConfig.Level <= clonePriceAsset.MaxLevel && cloneConfig.Level >= clonePriceAsset.MinLevel) ||
            !UtilitiesFunctions.IsCloneItem(digicloneItem.ItemInfo.Section))
        {
            await GameLogger.LogWarning($"Validación fallida: CloneLevel={cloneConfig.Level}, MinLevel={clonePriceAsset.MinLevel}, MaxLevel={clonePriceAsset.MaxLevel}, IsCloneItem={UtilitiesFunctions.IsCloneItem(digicloneItem.ItemInfo.Section)}", "digiclone");
            client.Send(new DigicloneResultPacket(DigicloneResultEnum.Fail, client.Partner.Digiclone).Serialize());
            client.Send(new SystemMessagePacket("Something went wrong."));
            return;
        }

            var cloneAsset = _assets.CloneValues.FirstOrDefault(x =>
                x.Type == cloneType && currentCloneLevel + 1 >= x.MinLevel && currentCloneLevel + 1 <= x.MaxLevel);
            if (cloneAsset == null)
            {
                await GameLogger.LogWarning($"Assets de clonación inválidos para CloneType={cloneType}, Level={currentCloneLevel + 1}", "digiclone");
                client.Send(UtilitiesFunctions.GroupPackets(
                    new SystemMessagePacket($"Invalid clone assets.").Serialize(),
                    new DigicloneResultPacket(DigicloneResultEnum.Fail, client.Partner.Digiclone).Serialize()
                ));
                return;
            }
            await GameLogger.LogInfo($"CloneAsset encontrado: Type={cloneAsset.Type}, MinValue={cloneAsset.MinValue}, MaxValue={cloneAsset.MaxValue}", "digiclone");

            var cloneResult = DigicloneResultEnum.Fail;
            short value = 0;

            if (clonePriceAsset.Reinforced)
            {
                var randomChance = UtilitiesFunctions.RandomDouble();
                if (cloneConfig.SuccessChance >= randomChance)
                {
                    cloneResult = DigicloneResultEnum.Success;
                    value = (short)cloneAsset.MaxValue;
                    await GameLogger.LogInfo($"Clone Reinforced: SuccessChance={cloneConfig.SuccessChance}, Random={randomChance}, Value={value}", "digiclone");
                }
                else
                {
                    await GameLogger.LogInfo($"Clone Reinforced fallido: SuccessChance={cloneConfig.SuccessChance}, Random={randomChance}", "digiclone");
                }
            }
            else if (clonePriceAsset.Mega)
            {
                cloneResult = DigicloneResultEnum.Success;
                value = UtilitiesFunctions.RandomShort((short)cloneAsset.MinValue, (short)cloneAsset.MaxValue);
                await GameLogger.LogInfo($"Clone Mega: RandomValue={value}, MinValue={cloneAsset.MinValue}, MaxValue={cloneAsset.MaxValue}", "digiclone");
            }
            else if (clonePriceAsset.MegaReinforced)
            {
                cloneResult = DigicloneResultEnum.Success;
                value = (short)cloneAsset.MaxValue;
                await GameLogger.LogInfo($"Clone MegaReinforced: Value={value}", "digiclone");
            }
            else if (clonePriceAsset.Low)
            {
                cloneResult = DigicloneResultEnum.Success;
                value = (short)cloneAsset.MinValue;
                await GameLogger.LogInfo($"Clone Low: Value={value}", "digiclone");
            }
            else
            {
                var randomChance = UtilitiesFunctions.RandomDouble();
                if (cloneConfig.SuccessChance >= randomChance)
                {
                    cloneResult = DigicloneResultEnum.Success;
                    value = UtilitiesFunctions.RandomShort((short)cloneAsset.MinValue, (short)cloneAsset.MaxValue);
                    await GameLogger.LogInfo($"Clone Estándar: SuccessChance={cloneConfig.SuccessChance}, Random={randomChance}, Value={value}", "digiclone");
                }
                else
                {
                    await GameLogger.LogInfo($"Clone Estándar fallido: SuccessChance={cloneConfig.SuccessChance}, Random={randomChance}", "digiclone");
                }
            }

            // Log del resultado del intento de clonación
            await GameLogger.LogInfo($"Resultado del intento de clonación: CloneResult={cloneResult}, Value={value}, SuccessChance={cloneConfig.SuccessChance}", "digiclone");

            var backupItem = client.Tamer.Inventory.FindItemBySlot(backupSlot);
            await GameLogger.LogInfo($"BackupItem: {(backupItem != null ? $"ItemId={backupItem.ItemId}, Slot={backupSlot}" : "Ninguno")}", "digiclone");

            if (cloneResult == DigicloneResultEnum.Success)
            {
                await GameLogger.LogInfo(
                    $"Tamer {client.TamerId} incrementó el nivel de clonación de {client.Partner.Id} (CloneType={cloneType}) a {currentCloneLevel + 1} con valor {value} usando ItemId={digicloneItem.ItemId}, BackupItemId={backupItem?.ItemId}", "digiclone");

                client.Partner.Digiclone.IncreaseCloneLevel(cloneType, value);
                await GameLogger.LogInfo($"Nivel de clonación incrementado: CloneType={cloneType}, NewLevel={client.Partner.Digiclone.GetCurrentLevel(cloneType)}", "digiclone");

                if (client.Partner.Digiclone.MaxCloneLevel)
                {
                    client.SendToAll(new NeonMessagePacket(
                            NeonMessageTypeEnum.Digimon,
                            client.Tamer.Name,
                            client.Partner.CurrentType,
                            client.Partner.Digiclone.CloneLevel - 1
                        ).Serialize());
                    await GameLogger.LogInfo($"Nivel máximo de clonación alcanzado: CloneLevel={client.Partner.Digiclone.CloneLevel}, Tamer={client.Tamer.Name}", "digiclone");
                }
            }
            else
            {
                if (cloneConfig.CanBreak && cloneConfig.BreakChance >= UtilitiesFunctions.RandomDouble())
                {
                    var breakChanceRandom = UtilitiesFunctions.RandomDouble();
                    await GameLogger.LogInfo($"Verificación de rotura: BreakChance={cloneConfig.BreakChance}, Random={breakChanceRandom}", "digiclone");

                    if (backupItem == null)
                    {
                        await GameLogger.LogWarning(
                            $"Tamer {client.TamerId} rompió el nivel de clonación de {client.Partner.Id} (CloneType={cloneType}) a {currentCloneLevel - 1} usando ItemId={digicloneItem.ItemId} sin backup", "digiclone");
                        cloneResult = DigicloneResultEnum.Break;
                        client.Partner.Digiclone.Break(cloneType);
                        await GameLogger.LogInfo($"Clonación rota: CloneType={cloneType}, NewLevel={client.Partner.Digiclone.GetCurrentLevel(cloneType)}", "digiclone");
                    }
                    else
                    {
                        await GameLogger.LogInfo(
                            $"Tamer {client.TamerId} falló en incrementar el nivel de clonación de {client.Partner.Id} (CloneType={cloneType}) a {currentCloneLevel + 1} usando ItemId={digicloneItem.ItemId} con backup ItemId={backupItem.ItemId}", "digiclone");
                        cloneResult = DigicloneResultEnum.Backup;
                    }
                }
                else
                {
                    await GameLogger.LogInfo(
                        $"Tamer {client.TamerId} falló en incrementar el nivel de clonación de {client.Partner.Id} (CloneType={cloneType}) a {currentCloneLevel + 1} usando ItemId={digicloneItem.ItemId}, BackupItemId={backupItem?.ItemId}", "digiclone");
                    cloneResult = DigicloneResultEnum.Fail;
                }
            }

            client.Send(new DigicloneResultPacket(cloneResult, client.Partner.Digiclone));
            await GameLogger.LogInfo($"Paquete DigicloneResult enviado: CloneResult={cloneResult}, DigicloneId={client.Partner.Digiclone?.Id}", "digiclone");

            if (cloneResult == DigicloneResultEnum.Success)
            {
                client.Send(new UpdateStatusPacket(client.Tamer));
                await GameLogger.LogInfo($"Paquete UpdateStatus enviado para TamerId={client.TamerId}", "digiclone");
            }

            client.Tamer.Inventory.RemoveBits(clonePriceAsset.Bits);
            await GameLogger.LogInfo($"Bits removidos: Bits={clonePriceAsset.Bits}, TamerId={client.TamerId}", "digiclone");

            client.Tamer.Inventory.RemoveOrReduceItem(digicloneItem, 1, digicloneSlot);
            await GameLogger.LogInfo($"Ítem de clonación removido: ItemId={digicloneItem.ItemId}, Slot={digicloneSlot}", "digiclone");

            client.Tamer.Inventory.RemoveOrReduceItem(backupItem, 1, backupSlot);
            await GameLogger.LogInfo($"BackupItem removido: {(backupItem != null ? $"ItemId={backupItem.ItemId}, Slot={backupSlot}" : "Ninguno")}", "digiclone");

            var updateDigicloneResult = await _sender.Send(new UpdateDigicloneCommand(client.Partner.Digiclone));
            if (updateDigicloneResult == null)
            {
                await GameLogger.LogError($"Fallo al guardar información del Digiclone: DigicloneId={client.Partner.Digiclone?.Id}", "digiclone");
            }
            else
            {
                await GameLogger.LogInfo($"Información del Digiclone guardada: DigicloneId={client.Partner.Digiclone?.Id}", "digiclone");
            }

            var updateBitsResult = await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));
            if (updateBitsResult == null)
            {
                await GameLogger.LogError($"Fallo al guardar información de bits: TamerId={client.TamerId}", "digiclone");
            }
            else
            {
                await GameLogger.LogInfo($"Información de bits guardada: TamerId={client.TamerId}", "digiclone");
            }

            var updateItemsResult = await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            if (updateItemsResult == null)
            {
                await GameLogger.LogError($"Fallo al guardar información de ítems: TamerId={client.TamerId}", "digiclone");
            }
            else
            {
                await GameLogger.LogInfo($"Información de ítems guardada: TamerId={client.TamerId}", "digiclone");
            }

            await GameLogger.LogInfo($"Fin del procesamiento del paquete PartnerDigiclone para TamerId={client.TamerId}", "digiclone");
        }
    }
}