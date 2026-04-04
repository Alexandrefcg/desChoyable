using System.Collections.Generic;

namespace DestroyChecker.Core.Data
{
    /// <summary>
    /// Item IDs that should never be destroyed, regardless of heuristic results.
    /// </summary>
    public static class KnownQuestItems
    {
        // Gifts used in Legendary weapons
        private static readonly HashSet<int> KeepAlways = new HashSet<int>
        {
            19676,  // Gift of Exploration
            19678,  // Gift of Battle
            19675,  // Gift of Mastery
            19677,  // Bloodstone Shard
            29185,  // Gift of Maguuma Mastery
            29186,  // Gift of Desert Mastery
            77449,  // Gift of Cantha
            46738,  // Chest of Loyalty (login reward)
            46731,  // Chest of Black Lion Goods
            19996,  // Mystic Coin
            19925,  // Mystic Clover
            46741,  // Laurel
            79726,  // Research Note
        };

        // Items that look safe but are NOT
        private static readonly HashSet<int> CheckFirst = new HashSet<int>
        {
            // Dungeon tokens that look like generic trophies
            // but are used to purchase equipment
        };

        public static bool ShouldKeep(int itemId) => KeepAlways.Contains(itemId);

        public static bool ShouldCheck(int itemId) => CheckFirst.Contains(itemId);
    }
}
