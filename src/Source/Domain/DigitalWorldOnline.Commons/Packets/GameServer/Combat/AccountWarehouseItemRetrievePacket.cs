using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer.Combat
{
    public class AccountWarehouseItemRetrievePacket : PacketWriter
    {
        private const int PacketNumber = 3931;

        /// </summary>
        /// <param name="giftStorage">The list of Gift Storage</param>
        public AccountWarehouseItemRetrievePacket(ItemModel item, int wareSlot, InventoryTypeEnum inventoryType, int result)
        {
            Type(PacketNumber);

            WriteInt(result);                       // Result
            WriteInt((int)item.RemainingMinutes()); // Time remaining (seconds)
            WriteShort((short)wareSlot);            // CashWarehouse slot

            WriteByte((byte)inventoryType);         // Inventory (0)
            WriteByte((byte)item.Slot);             // m_nSlotNo
            WriteInt(item.ItemId);                  // m_nItemType
            WriteInt(item.Amount);                  // m_nItemCount
            WriteByte(0);                           // m_nItemRate
            WriteInt(0);                            // m_nItemEndTime
            WriteInt(0);                            // m_nRemainTradeLimitTime
        }
    }
}