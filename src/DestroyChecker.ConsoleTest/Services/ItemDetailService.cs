using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DestroyChecker.Core.Models;

namespace DestroyChecker.ConsoleTest.Services;

public class ItemDetailService
{
    private readonly HttpClient _http;
    private readonly Dictionary<int, ItemInfo> _cache = new();
    private const int BatchSize = 200;

    public ItemDetailService(HttpClient http)
    {
        _http = http;
    }

    public async Task<Dictionary<int, ItemInfo>> GetItemDetailsAsync(IEnumerable<int> itemIds)
    {
        var uniqueIds = itemIds.Distinct().ToList();
        var uncachedIds = uniqueIds.Where(id => !_cache.ContainsKey(id)).ToList();

        // Fetch in batches of 200
        for (int i = 0; i < uncachedIds.Count; i += BatchSize)
        {
            var batch = uncachedIds.Skip(i).Take(BatchSize);
            var idsParam = string.Join(",", batch);
            var url = $"https://api.guildwars2.com/v2/items?ids={idsParam}";

            try
            {
                var items = await _http.GetFromJsonAsync<List<ApiItem>>(url, _jsonOptions);
                if (items is null) continue;

                foreach (var item in items)
                {
                    var info = new ItemInfo
                    {
                        Id = item.Id,
                        Name = item.Name ?? $"Item #{item.Id}",
                        Type = item.Type ?? "Unknown",
                        Rarity = item.Rarity ?? "Unknown",
                        VendorValue = item.VendorValue,
                        Description = item.Description,
                        Flags = item.Flags ?? new List<string>(),
                        IconUrl = item.Icon
                    };
                    _cache[item.Id] = info;
                }
            }
            catch (HttpRequestException)
            {
                // API unavailable for this batch — continue with next
            }
        }

        return uniqueIds
            .Where(id => _cache.ContainsKey(id))
            .ToDictionary(id => id, id => _cache[id]);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record ApiItem(
        int Id,
        string? Name,
        string? Type,
        string? Rarity,
        [property: JsonPropertyName("vendor_value")] int VendorValue,
        string? Description,
        List<string>? Flags,
        string? Icon
    );
}
