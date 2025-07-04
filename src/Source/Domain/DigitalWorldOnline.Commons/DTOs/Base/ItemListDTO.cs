using DigitalWorldOnline.Commons.Enums;

namespace DigitalWorldOnline.Commons.DTOs.Base
{
public sealed class ItemListDTO
{
    public long Id { get; set; }
    public ItemListEnum  Type { get; set; }
    public byte Size { get; set; }
    public long Bits { get; set; }

    public long? AccountId { get; set; }
    public long? CharacterId { get; set; } 

    public List<ItemDTO> Items { get; set; }
}

}