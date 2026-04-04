namespace DestroyChecker.Core.Models
{
    public class CollectionEntry
    {
        public int AchievementId { get; set; }
        public string CollectionName { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int Current { get; set; }
        public int Max { get; set; }
    }
}
