using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Application.Separar.Queries;
using MediatR;
using Serilog;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.Items;
using GameServer.Logging; // Importamos el namespace del GameLogger
using System.Linq;
using System.Threading.Tasks;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Enums.ClientEnums;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class CashShopBuyPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.CashShopBuy;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;

        public CashShopBuyPacketProcessor(
            ILogger logger,
            AssetsLoader assets,
            ISender sender)
        {
            _logger = logger;
            _assets = assets;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            int amount = packet.ReadByte();
            int price = packet.ReadInt();
            int type = packet.ReadInt();
            int u1 = packet.ReadInt();

            bool comprado = false;
            short Result = 1;
            sbyte TotalSuccess = 0;
            sbyte TotalFail = 0;

            int[] unique_id = new int[amount];
            List<int> success_id = new List<int>();
            List<int> fail_id = new List<int>();

            // Log: Inicio de la transacción
            await GameLogger.LogInfo(
                $"Tamer: {client.Tamer.Name}, Attempting to buy {amount} items for {price} Premium",
                "CashShop");

            if (client.Premium >= price)
            {
                for (int u = 0; u < amount; u++)
                {
                    unique_id[u] = packet.ReadInt();
                    var Quexi = _assets.CashShopAssets.FirstOrDefault(x => x.Unique_Id == unique_id[u]);

                    if (Quexi != null && Quexi.Activated == 1)
                    {
                        // Log: Procesando ítem individual
                        await GameLogger.LogInfo(
                            $"Tamer: {client.Tamer.Name}, Processing Item ID: {Quexi.Item_Id}, Name: {Quexi.ItemName}, Quantity: {Quexi.Quanty}, Price: {Quexi.Price}",
                            "CashShop");

                        if (client.Premium >= Quexi.Price)
                        {
                            var itemId = Quexi.Item_Id;

                            var newItem = new ItemModel();
                            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                            newItem.ItemId = itemId;
                            newItem.Amount = Quexi.Quanty;

                            if (newItem.IsTemporary)
                                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                            var itemClone = (ItemModel)newItem.Clone();

                            if (client.Tamer.Inventory.AddItem(newItem))
                            {
                                client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                                comprado = true;
                                Result = 0;
                                TotalSuccess++;
                                success_id.Add(Quexi.Item_Id);
                                client.Premium -= Quexi.Price;

                                // Log: Compra exitosa de ítem
                                await GameLogger.LogInfo(
                                    $"Tamer: {client.Tamer.Name}, Successfully bought Item ID: {Quexi.Item_Id}, New Premium: {client.Premium}",
                                    "CashShop");
                            }
                            else
                            {
                                TotalFail++;
                                fail_id.Add(Quexi.Item_Id);
                                // Log: Fallo al añadir ítem al inventario
                                await GameLogger.LogWarning(
                                    $"Tamer: {client.Tamer.Name}, Failed to add Item ID: {Quexi.Item_Id} to inventory",
                                    "CashShop");
                            }
                        }
                        else
                        {
                            TotalFail++;
                            fail_id.Add(Quexi.Item_Id);
                            // Log: Saldo insuficiente para ítem individual
                            await GameLogger.LogWarning(
                                $"Tamer: {client.Tamer.Name}, Insufficient Premium ({client.Premium}) for Item ID: {Quexi.Item_Id}, Price: {Quexi.Price}",
                                "CashShop");
                            client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
                        }
                    }
                    else
                    {
                        TotalFail++;
                        fail_id.Add(unique_id[u]);
                        // Log: Ítem no válido o no activado
                        await GameLogger.LogWarning(
                            $"Tamer: {client.Tamer.Name}, Invalid or inactive Item Unique ID: {unique_id[u]}",
                            "CashShop");
                        client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
                    }
                }

                await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));

                if (comprado)
                {
                    // Log: Transacción exitosa
                    await GameLogger.LogInfo(
                        $"Tamer: {client.Tamer.Name}, Purchase completed. Success: {TotalSuccess}, Fail: {TotalFail}, Final Premium: {client.Premium}, Final Silk: {client.Silk}",
                        "CashShop");
                    client.Send(new CashShopReturnPacket(Result, client.Premium, client.Silk, TotalSuccess, TotalFail));
                }
            }
            else
            {
                // Log: Saldo insuficiente para la transacción
                await GameLogger.LogWarning(
                    $"Tamer: {client.Tamer.Name}, Insufficient Premium ({client.Premium}) for total Price: {price}",
                    "CashShop");
                client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
            }
        }
    }
}