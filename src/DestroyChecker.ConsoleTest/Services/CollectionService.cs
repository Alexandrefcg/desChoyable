using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DestroyChecker.Core.Models;
using DestroyChecker.Core.Services;

namespace DestroyChecker.ConsoleTest.Services;

public class CollectionService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private Dictionary<int, List<CollectionEntry>>? _itemCollectionMap;

    public CollectionService(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public async Task InitializeAsync()
    {
        // 1. Fetch achievement groups to find "Collections" group
        var groups = await _http.GetFromJsonAsync<List<AchievementGroupDto>>(
            "https://api.guildwars2.com/v2/achievements/groups?ids=all");
        if (groups == null) return;

        var collectionCategoryIds = new HashSet<int>();
        foreach (var group in groups)
        {
            if (group.Name != null &&
                group.Name.Contains("Collection", StringComparison.OrdinalIgnoreCase)
                && group.Categories != null)
            {
                foreach (var catId in group.Categories)
                    collectionCategoryIds.Add(catId);
            }
        }

        if (collectionCategoryIds.Count == 0) return;

        // 2. Fetch category details to get achievement IDs
        var achievementIds = new HashSet<int>();
        foreach (var batch in Chunk(collectionCategoryIds.ToList(), 200))
        {
            var ids = string.Join(",", batch);
            try
            {
                var categories = await _http.GetFromJsonAsync<List<CategoryDto>>(
                    $"https://api.guildwars2.com/v2/achievements/categories?ids={ids}");
                if (categories == null) continue;
                foreach (var cat in categories)
                    if (cat.Achievements != null)
                        foreach (var achId in cat.Achievements)
                            achievementIds.Add(achId);
            }
            catch { }
        }

        if (achievementIds.Count == 0) return;

        // 3. Fetch achievement details in batches
        var achievements = new List<AchievementData>();
        foreach (var batch in Chunk(achievementIds.ToList(), 200))
        {
            var ids = string.Join(",", batch);
            try
            {
                var results = await _http.GetFromJsonAsync<List<AchievementDto>>(
                    $"https://api.guildwars2.com/v2/achievements?ids={ids}");
                if (results == null) continue;
                achievements.AddRange(results.Select(MapToAchievementData));
            }
            catch { }
        }

        // 4. Fetch account progress
        var progress = new List<AccountAchievementProgress>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.guildwars2.com/v2/account/achievements");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var accountAchievements = await response.Content.ReadFromJsonAsync<List<AccountAchievementDto>>();
            if (accountAchievements != null)
            {
                progress.AddRange(accountAchievements.Select(a => new AccountAchievementProgress
                {
                    Id = a.Id,
                    Done = a.Done,
                    Current = a.Current ?? 0,
                    Max = a.Max ?? 0
                }));
            }
        }
        catch { }

        // 5. Build map using Core's CollectionMapper
        _itemCollectionMap = CollectionMapper.BuildItemCollectionMap(achievements, progress);
    }

    public List<CollectionEntry> GetCollectionsForItem(int itemId)
    {
        if (_itemCollectionMap == null)
            return new List<CollectionEntry>();

        return _itemCollectionMap.TryGetValue(itemId, out var entries)
            ? entries
            : new List<CollectionEntry>();
    }

    public Dictionary<int, List<CollectionEntry>> GetCollectionMap()
    {
        return _itemCollectionMap ?? new Dictionary<int, List<CollectionEntry>>();
    }

    private static AchievementData MapToAchievementData(AchievementDto dto)
    {
        return new AchievementData
        {
            Id = dto.Id,
            Name = dto.Name ?? $"Achievement {dto.Id}",
            Type = dto.Type ?? string.Empty,
            Bits = dto.Bits?.Select(b => new AchievementBit
            {
                Type = b.Type ?? string.Empty,
                Id = b.Id
            }).ToList() ?? new List<AchievementBit>()
        };
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int chunkSize)
    {
        for (int i = 0; i < source.Count; i += chunkSize)
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
    }

    // DTOs for JSON deserialization
    private class AchievementGroupDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("categories")] public List<int>? Categories { get; set; }
    }

    private class CategoryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("achievements")] public List<int>? Achievements { get; set; }
    }

    private class AchievementDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("bits")] public List<AchievementBitDto>? Bits { get; set; }
    }

    private class AchievementBitDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("id")] public int? Id { get; set; }
    }

    private class AccountAchievementDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
        [JsonPropertyName("current")] public int? Current { get; set; }
        [JsonPropertyName("max")] public int? Max { get; set; }
    }
}
