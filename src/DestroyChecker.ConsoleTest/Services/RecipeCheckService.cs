using System.Net.Http.Json;

namespace DestroyChecker.ConsoleTest.Services;

public class RecipeCheckService
{
    private readonly HttpClient _http;
    private readonly Dictionary<int, int> _cache = new();
    private readonly SemaphoreSlim _throttle = new(5, 5);
    private const int ThrottleDelayMs = 250;

    public RecipeCheckService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(bool usedInRecipes, int recipeCount)> CheckItemRecipesAsync(int itemId)
    {
        if (_cache.TryGetValue(itemId, out var cached))
            return (cached > 0, cached);

        await _throttle.WaitAsync();
        try
        {
            var url = $"https://api.guildwars2.com/v2/recipes/search?input={itemId}";
            var recipeIds = await _http.GetFromJsonAsync<List<int>>(url);
            var count = recipeIds?.Count ?? 0;
            _cache[itemId] = count;
            return (count > 0, count);
        }
        catch (HttpRequestException)
        {
            _cache[itemId] = 0;
            return (false, 0);
        }
        finally
        {
            _throttle.Release();
            await Task.Delay(ThrottleDelayMs);
        }
    }

    public async Task BulkCheckRecipesAsync(IEnumerable<int> itemIds)
    {
        var unchecked_ = itemIds.Where(id => !_cache.ContainsKey(id)).Distinct().ToList();

        // Process in parallel respecting throttle (semaphore limits to 5)
        var tasks = unchecked_.Select(id => CheckItemRecipesAsync(id));
        await Task.WhenAll(tasks);
    }
}
