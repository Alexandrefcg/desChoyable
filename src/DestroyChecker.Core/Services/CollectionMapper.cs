using System;
using System.Collections.Generic;
using System.Linq;
using DestroyChecker.Core.Models;

namespace DestroyChecker.Core.Services
{
    /// <summary>
    /// Pure logic: builds a map of itemId → CollectionEntry list from pre-fetched achievement data.
    /// No HTTP calls — frontends are responsible for fetching the data.
    /// </summary>
    public static class CollectionMapper
    {
        public static Dictionary<int, List<CollectionEntry>> BuildItemCollectionMap(
            IEnumerable<AchievementData> achievements,
            IEnumerable<AccountAchievementProgress> accountProgress)
        {
            var progressLookup = new Dictionary<int, AccountAchievementProgress>();
            foreach (var p in accountProgress)
                progressLookup[p.Id] = p;

            var map = new Dictionary<int, List<CollectionEntry>>();

            foreach (var achievement in achievements)
            {
                if (!achievement.Type.Equals("ItemSet", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (achievement.Bits == null || achievement.Bits.Count == 0)
                    continue;

                progressLookup.TryGetValue(achievement.Id, out var progress);
                var entry = new CollectionEntry
                {
                    AchievementId = achievement.Id,
                    CollectionName = achievement.Name,
                    IsCompleted = progress?.Done ?? false,
                    Current = progress?.Current ?? 0,
                    Max = progress?.Max ?? achievement.Bits.Count
                };

                foreach (var bit in achievement.Bits)
                {
                    if (bit.Type.Equals("Item", StringComparison.OrdinalIgnoreCase) && bit.Id.HasValue)
                    {
                        if (!map.ContainsKey(bit.Id.Value))
                            map[bit.Id.Value] = new List<CollectionEntry>();

                        map[bit.Id.Value].Add(entry);
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Applies collection data to an ItemInfo, setting BelongsToCollection, CollectionNames, etc.
        /// </summary>
        public static void ApplyCollectionInfo(ItemInfo item, Dictionary<int, List<CollectionEntry>> collectionMap)
        {
            if (!collectionMap.TryGetValue(item.Id, out var collections) || collections.Count == 0)
                return;

            item.BelongsToCollection = true;
            item.CollectionNames = collections.Select(c => c.CollectionName).Distinct().ToList();
            item.AllCollectionsCompleted = collections.All(c => c.IsCompleted);

            var incomplete = collections.Where(c => !c.IsCompleted).ToList();
            if (incomplete.Count > 0)
                item.CollectionProgress = $"{incomplete[0].Current}/{incomplete[0].Max}";
        }
    }
}
