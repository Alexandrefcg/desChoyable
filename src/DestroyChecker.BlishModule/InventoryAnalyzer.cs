using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules.Managers;
using DestroyChecker.Core.Models;
using DestroyChecker.Core.Services;
using Gw2Sharp.WebApi.V2.Models;
using CoreAchievementBit = DestroyChecker.Core.Models.AchievementBit;

namespace DestroyChecker.BlishModule
{
    /// <summary>
    /// Uses Blish HUD's Gw2ApiManager to fetch inventory data and classify items.
    /// Scans only the current in-game character via Mumble.
    /// </summary>
    public class InventoryAnalyzer
    {
        private static readonly Logger Logger = Logger.GetLogger<InventoryAnalyzer>();

        private readonly Gw2ApiManager _apiManager;
        private readonly ItemClassifier _classifier = new ItemClassifier();
        private readonly SemaphoreSlim _recipeSemaphore = new SemaphoreSlim(10, 10);

        // Caches
        private Dictionary<int, List<CollectionEntry>>? _collectionMapCache;
        private DateTime _collectionCacheExpiry = DateTime.MinValue;
        private readonly Dictionary<int, (bool Used, int Count)> _recipeCache = new Dictionary<int, (bool, int)>();
        private readonly Dictionary<int, ItemInfo> _itemDetailCache = new Dictionary<int, ItemInfo>();
        private string? _lastInventoryHash;
        private static readonly TimeSpan CollectionCacheDuration = TimeSpan.FromMinutes(10);

        public InventoryAnalyzer(Gw2ApiManager apiManager)
        {
            _apiManager = apiManager;
        }

        public string? LastScannedCharacter { get; private set; }
        public List<ItemInfo> LastResults { get; private set; } = new List<ItemInfo>();
        public bool IsLoading { get; private set; }

        public async Task<List<ItemInfo>> AnalyzeCurrentCharacterAsync()
        {
            if (IsLoading) return LastResults;
            IsLoading = true;

            try
            {
                var characterName = GameService.Gw2Mumble.PlayerCharacter.Name;
                if (string.IsNullOrEmpty(characterName))
                {
                    Logger.Warn("No character name available from Mumble.");
                    return LastResults;
                }

                var requiredPermissions = new List<TokenPermission>
                {
                    TokenPermission.Account,
                    TokenPermission.Characters,
                    TokenPermission.Inventories,
                    TokenPermission.Progression
                };

                if (!_apiManager.HasPermissions(requiredPermissions))
                {
                    Logger.Warn("Missing API permissions for inventory analysis.");
                    return LastResults;
                }

                // 1. Fetch character inventory
                var slots = await FetchCharacterInventoryAsync(characterName);
                if (slots.Count == 0)
                {
                    LastScannedCharacter = characterName;
                    LastResults = new List<ItemInfo>();
                    _lastInventoryHash = null;
                    return LastResults;
                }

                // 1b. Check if inventory changed since last scan
                var inventoryHash = ComputeInventoryHash(slots);
                if (inventoryHash == _lastInventoryHash && characterName == LastScannedCharacter && LastResults.Count > 0)
                {
                    Logger.Info("Inventory unchanged, skipping full analysis.");
                    return LastResults;
                }
                _lastInventoryHash = inventoryHash;

                // 2. Fetch item details (with cache)
                var uniqueIds = slots.Select(s => s.ItemId).Distinct().ToList();
                var itemDetails = await FetchItemDetailsAsync(uniqueIds);

                // 3. Filter Trophy/Gizmo
                var analyzable = itemDetails.Values
                    .Where(i => _classifier.ShouldAnalyze(i))
                    .ToList();

                // 4. Aggregate counts
                foreach (var item in analyzable)
                {
                    var matching = slots.Where(s => s.ItemId == item.Id).ToList();
                    item.TotalCount = matching.Sum(s => s.Count);
                    item.FoundOnCharacters = new List<string> { characterName };
                }

                // 5. Check recipes
                await CheckRecipesAsync(analyzable);

                // 6. Check collections
                await CheckCollectionsAsync(analyzable);

                // 7. Classify
                foreach (var item in analyzable)
                    _classifier.Classify(item);

                LastScannedCharacter = characterName;
                LastResults = analyzable
                    .OrderBy(i => i.Safety)
                    .ThenBy(i => i.Rarity)
                    .ThenBy(i => i.Name)
                    .ToList();

                return LastResults;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Inventory analysis failed: {ex.Message}");
                return LastResults;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<List<InventorySlot>> FetchCharacterInventoryAsync(string characterName)
        {
            var slots = new List<InventorySlot>();

            try
            {
                var character = await _apiManager.Gw2ApiClient.V2.Characters[characterName].Inventory.GetAsync();
                if (character?.Bags == null) return slots;

                foreach (var bag in character.Bags)
                {
                    if (bag?.Inventory == null) continue;
                    foreach (var item in bag.Inventory)
                    {
                        if (item == null) continue;
                        slots.Add(new InventorySlot
                        {
                            ItemId = item.Id,
                            Count = item.Count,
                            CharacterName = characterName,
                            IsSharedInventory = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"Failed to fetch inventory for {characterName}: {ex.Message}");
            }

            return slots;
        }

        private async Task<Dictionary<int, ItemInfo>> FetchItemDetailsAsync(List<int> itemIds)
        {
            var result = new Dictionary<int, ItemInfo>();

            // Return cached items and collect uncached IDs
            var uncachedIds = new List<int>();
            foreach (var id in itemIds)
            {
                if (_itemDetailCache.TryGetValue(id, out var cached))
                    result[id] = CloneItemInfo(cached);
                else
                    uncachedIds.Add(id);
            }

            // Fetch only uncached items from API
            foreach (var batch in Chunk(uncachedIds, 200))
            {
                try
                {
                    var items = await _apiManager.Gw2ApiClient.V2.Items.ManyAsync(batch);
                    foreach (var item in items)
                    {
                        var info = new ItemInfo
                        {
                            Id = item.Id,
                            Name = item.Name ?? $"Item #{item.Id}",
                            Type = item.Type.ToString() ?? "Unknown",
                            Rarity = item.Rarity.ToString() ?? "Unknown",
                            VendorValue = item.VendorValue,
                            Description = item.Description,
                            Flags = item.Flags?.Select(f => f.ToString() ?? string.Empty).Where(f => f.Length > 0).ToList() ?? new List<string>(),
                            IconUrl = item.Icon.Url?.ToString()
                        };
                        _itemDetailCache[item.Id] = info;
                        result[item.Id] = CloneItemInfo(info);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"Failed to fetch item details batch: {ex.Message}");
                }
            }

            return result;
        }

        private static ItemInfo CloneItemInfo(ItemInfo source)
        {
            return new ItemInfo
            {
                Id = source.Id,
                Name = source.Name,
                Type = source.Type,
                Rarity = source.Rarity,
                VendorValue = source.VendorValue,
                Description = source.Description,
                Flags = new List<string>(source.Flags),
                IconUrl = source.IconUrl
            };
        }

        private async Task CheckRecipesAsync(List<ItemInfo> items)
        {
            var uncached = items.Where(i => !_recipeCache.ContainsKey(i.Id)).ToList();
            var tasks = uncached.Select(item => CheckSingleRecipeAsync(item)).ToList();
            await Task.WhenAll(tasks);

            // Apply cached results to all items
            foreach (var item in items)
            {
                if (_recipeCache.TryGetValue(item.Id, out var cached))
                {
                    item.UsedInRecipes = cached.Used;
                    item.RecipeCount = cached.Count;
                }
            }
        }

        private async Task CheckSingleRecipeAsync(ItemInfo item)
        {
            await _recipeSemaphore.WaitAsync();
            try
            {
                var recipes = await _apiManager.Gw2ApiClient.V2.Recipes.Search.Input(item.Id).GetAsync();
                var count = recipes?.Count() ?? 0;
                _recipeCache[item.Id] = (count > 0, count);
            }
            catch (Exception ex)
            {
                Logger.Info($"Failed to check recipes for item {item.Id}: {ex.Message}");
                _recipeCache[item.Id] = (false, 0);
            }
            finally
            {
                _recipeSemaphore.Release();
            }
        }

        private async Task CheckCollectionsAsync(List<ItemInfo> items)
        {
            try
            {
                if (_collectionMapCache != null && DateTime.UtcNow < _collectionCacheExpiry)
                {
                    foreach (var item in items)
                        CollectionMapper.ApplyCollectionInfo(item, _collectionMapCache);
                    return;
                }

                // Fetch achievement groups
                var groups = await _apiManager.Gw2ApiClient.V2.Achievements.Groups.AllAsync();
                var collectionCategoryIds = new HashSet<int>();
                foreach (var group in groups)
                {
                    if (group.Name != null &&
                        group.Name.IndexOf("Collection", StringComparison.OrdinalIgnoreCase) >= 0
                        && group.Categories != null)
                    {
                        foreach (var catId in group.Categories)
                            collectionCategoryIds.Add(catId);
                    }
                }

                if (collectionCategoryIds.Count == 0) return;

                // Fetch categories
                var achievementIds = new HashSet<int>();
                var categories = await _apiManager.Gw2ApiClient.V2.Achievements.Categories.ManyAsync(collectionCategoryIds);
                foreach (var cat in categories)
                {
                    if (cat.Achievements != null)
                        foreach (var achId in cat.Achievements)
                            achievementIds.Add(achId);
                }

                if (achievementIds.Count == 0) return;

                // Fetch achievement details in batches
                var achievementDataList = new List<AchievementData>();
                foreach (var batch in Chunk(achievementIds.ToList(), 200))
                {
                    try
                    {
                        var achievements = await _apiManager.Gw2ApiClient.V2.Achievements.ManyAsync(batch);
                        foreach (var ach in achievements)
                        {
                            achievementDataList.Add(new AchievementData
                            {
                                Id = ach.Id,
                                Name = ach.Name ?? $"Achievement {ach.Id}",
                                Type = ach.Type?.ToString() ?? string.Empty,
                                Bits = ach.Bits?.OfType<AchievementItemBit>().Select(b => new CoreAchievementBit
                                {
                                    Type = "Item",
                                    Id = b.Id
                                }).ToList() ?? new List<CoreAchievementBit>()
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"Failed to fetch achievements batch: {ex.Message}");
                    }
                }

                // Fetch account progress
                var progressList = new List<AccountAchievementProgress>();
                try
                {
                    var accountAchievements = await _apiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();
                    foreach (var aa in accountAchievements)
                    {
                        progressList.Add(new AccountAchievementProgress
                        {
                            Id = aa.Id,
                            Done = aa.Done,
                            Current = aa.Current,
                            Max = aa.Max
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"Failed to fetch account achievements: {ex.Message}");
                }

                // Build map, cache, and apply
                _collectionMapCache = CollectionMapper.BuildItemCollectionMap(achievementDataList, progressList);
                _collectionCacheExpiry = DateTime.UtcNow + CollectionCacheDuration;

                foreach (var item in items)
                    CollectionMapper.ApplyCollectionInfo(item, _collectionMapCache);
            }
            catch (Exception ex)
            {
                Logger.Info($"Failed to load collections: {ex.Message}");
            }
        }

        private static string ComputeInventoryHash(List<InventorySlot> slots)
        {
            // Build a deterministic string from item IDs and counts
            var sorted = slots.OrderBy(s => s.ItemId).ThenBy(s => s.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in sorted)
                sb.Append(s.ItemId).Append(':').Append(s.Count).Append(';');
            return sb.ToString();
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> source, int chunkSize)
        {
            for (int i = 0; i < source.Count; i += chunkSize)
                yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }
    }
}
