namespace DestroyChecker.Core.Models
{
    public class InventorySlot
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public bool IsSharedInventory { get; set; }
    }
}
