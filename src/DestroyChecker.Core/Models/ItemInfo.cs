using System;
using System.Collections.Generic;
using System.Linq;

namespace DestroyChecker.Core.Models
{
    public class ItemInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int VendorValue { get; set; }
        public string? Description { get; set; }
        public List<string> Flags { get; set; } = new List<string>();
        public string? IconUrl { get; set; }

        // Classification result
        public ItemSafety Safety { get; set; }
        public string SafetyReason { get; set; } = string.Empty;

        // Aggregated inventory data
        public int TotalCount { get; set; }
        public List<string> FoundOnCharacters { get; set; } = new List<string>();

        // Recipes
        public bool UsedInRecipes { get; set; }
        public int RecipeCount { get; set; }

        // Collections
        public bool BelongsToCollection { get; set; }
        public List<string> CollectionNames { get; set; } = new List<string>();
        public bool AllCollectionsCompleted { get; set; }
        public string? CollectionProgress { get; set; }

        // Helpers
        public bool HasFlag(string flag) => Flags.Any(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase));

        public bool IsAccountBound => HasFlag("AccountBound") || HasFlag("AccountBindOnUse");
        public bool IsSoulBound => HasFlag("SoulbindOnAcquire") || HasFlag("SoulBindOnUse");
        public bool HasDeleteWarning => HasFlag("DeleteWarning");
        public bool IsNoSell => HasFlag("NoSell");
        public bool IsNoSalvage => HasFlag("NoSalvage");
        public bool IsUnique => HasFlag("Unique");
    }
}
