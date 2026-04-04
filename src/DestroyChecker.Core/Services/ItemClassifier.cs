using System;
using System.Collections.Generic;
using System.Linq;
using DestroyChecker.Core.Data;
using DestroyChecker.Core.Models;

namespace DestroyChecker.Core.Services
{
    public class ItemClassifier
    {
        private enum RarityTier { Low, Fine, Mid, High, Unknown }

        private static RarityTier GetRarityTier(string rarity)
        {
            switch (rarity?.ToLowerInvariant())
            {
                case "junk":
                case "basic":
                    return RarityTier.Low;
                case "fine":
                    return RarityTier.Fine;
                case "rare":
                case "masterwork":
                    return RarityTier.Mid;
                case "exotic":
                case "ascended":
                case "legendary":
                    return RarityTier.High;
                default:
                    return RarityTier.Unknown;
            }
        }

        private static readonly HashSet<string> AnalyzedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Trophy", "Gizmo"
        };

        public bool ShouldAnalyze(ItemInfo item)
        {
            return AnalyzedTypes.Contains(item.Type);
        }

        public void Classify(ItemInfo item)
        {
            var tier = GetRarityTier(item.Rarity);

            // 1. Known essential items — never destroy
            if (KnownQuestItems.ShouldKeep(item.Id))
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = "Known essential item (internal list)";
                return;
            }

            if (KnownQuestItems.ShouldCheck(item.Id))
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = "Item flagged for manual verification";
                return;
            }

            // 2. DeleteWarning flag — always keep
            if (item.HasDeleteWarning)
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = "DeleteWarning flag (game warns on delete)";
                return;
            }

            // 3. Incomplete collection — keep
            if (item.BelongsToCollection && !item.AllCollectionsCompleted)
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = $"Part of incomplete collection: {string.Join(", ", item.CollectionNames)}"
                    + (item.CollectionProgress != null ? $" ({item.CollectionProgress})" : "");
                return;
            }

            // 4. Completed collection — safe to destroy
            if (item.BelongsToCollection && item.AllCollectionsCompleted)
            {
                item.Safety = ItemSafety.Safe;
                item.SafetyReason = $"Collection completed: {string.Join(", ", item.CollectionNames)}";
                return;
            }

            // 5. High rarity — keep
            if (tier == RarityTier.High)
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = $"{item.Rarity} rarity — valuable item";
                return;
            }

            // 6. Used in crafting recipes — keep
            if (item.UsedInRecipes)
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = $"Used in {item.RecipeCount} crafting recipe(s)";
                return;
            }

            // 7. AccountBound + NoSell + mid rarity — likely quest/collection item
            if (item.IsAccountBound && item.IsNoSell && tier == RarityTier.Mid)
            {
                item.Safety = ItemSafety.Keep;
                item.SafetyReason = "AccountBound + NoSell + mid rarity — possible quest/collection item";
                return;
            }

            // 8. Gizmo type — check (may be temporary quest item)
            if (item.Type.Equals("Gizmo", StringComparison.OrdinalIgnoreCase))
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = "Gizmo type — may be a quest tool or temporary item";
                return;
            }

            // 9. Mid rarity without recipe — check
            if (tier == RarityTier.Mid)
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = $"{item.Rarity} rarity with no recipe use — verify before destroying";
                return;
            }

            // 10. AccountBound without NoSell — check
            if (item.IsAccountBound && !item.IsNoSell)
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = "AccountBound but sellable — check vendor value";
                return;
            }

            // 11. NoSell without AccountBound — check
            if (item.IsNoSell && !item.IsAccountBound)
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = "NoSell — verify if it has another use";
                return;
            }

            // 12. High vendor value — consider selling instead
            if (item.VendorValue > 100 && !item.IsNoSell)
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = $"Vendor value {FormatCoins(item.VendorValue)} — consider selling instead";
                return;
            }

            // 13. Salvageable Item - Consider Salvaging instead
            if (!item.IsNoSalvage)
            {
                item.Safety = ItemSafety.Check;
                item.SafetyReason = $"Salvageable item - consider salvaging instead";
                return;
            }

            // 14. Low rarity (Junk/Basic) — safe to destroy
            if (tier == RarityTier.Low)
            {
                item.Safety = ItemSafety.Safe;
                item.SafetyReason = $"Generic {item.Rarity} trophy — safe to destroy";
                return;
            }

            // 15. Fine without special flags — safe
            if (tier == RarityTier.Fine && !item.IsAccountBound && !item.IsNoSell && !item.IsUnique)
            {
                item.Safety = ItemSafety.Safe;
                item.SafetyReason = "Fine trophy with no restrictions — safe to destroy";
                return;
            }

            // Default: check manually
            item.Safety = ItemSafety.Check;
            item.SafetyReason = "Could not classify automatically — verify manually";
        }

        public static string FormatCoins(int copper)
        {
            var gold = copper / 10000;
            var silver = (copper % 10000) / 100;
            var cop = copper % 100;

            var parts = new List<string>();
            if (gold > 0) parts.Add($"{gold}g");
            if (silver > 0) parts.Add($"{silver}s");
            if (cop > 0 || parts.Count == 0) parts.Add($"{cop}c");
            return string.Join(" ", parts);
        }
    }
}
