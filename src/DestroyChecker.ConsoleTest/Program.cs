using System.Text.Json;
using System.Text.Json.Serialization;
using DestroyChecker.Core.Models;
using DestroyChecker.Core.Services;
using DestroyChecker.ConsoleTest.Services;

var apiKey = Environment.GetEnvironmentVariable("GW2_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Error: GW2_API_KEY environment variable is not set.");
    Console.WriteLine("Usage: export GW2_API_KEY=\"your-key-here\" && dotnet run");
    Console.ResetColor();
    return 1;
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║    DestroyChecker — GW2 Inventory Analyzer  ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "DestroyChecker/0.1");

var inventoryService = new InventoryService(http, apiKey);
var itemDetailService = new ItemDetailService(http);
var recipeCheckService = new RecipeCheckService(http);
var collectionService = new CollectionService(http, apiKey);
var classifier = new ItemClassifier();

// 1. Fetch characters
Console.Write("Fetching characters... ");
List<string> characters;
try
{
    characters = await inventoryService.GetCharacterNamesAsync();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"{characters.Count} found");
    Console.ResetColor();
    foreach (var name in characters)
        Console.WriteLine($"  • {name}");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Failed: {ex.Message}");
    Console.ResetColor();
    return 1;
}
Console.WriteLine();

// 2. Fetch inventories
Console.Write("Fetching inventories... ");
var slots = await inventoryService.GetAllInventorySlotsAsync();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"{slots.Count} item slots found");
Console.ResetColor();
Console.WriteLine();

// 3. Fetch item details
var uniqueItemIds = slots.Select(s => s.ItemId).Distinct().ToList();
Console.Write($"Fetching details for {uniqueItemIds.Count} unique items... ");
var itemDetails = await itemDetailService.GetItemDetailsAsync(uniqueItemIds);
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("OK");
Console.ResetColor();

// 4. Filter Trophy/Gizmo only
var analyzableItems = itemDetails.Values
    .Where(item => classifier.ShouldAnalyze(item))
    .ToList();

Console.WriteLine($"Trophy/Gizmo items found: {analyzableItems.Count}");
Console.WriteLine();

if (analyzableItems.Count == 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("No trophies or gizmos found in inventory.");
    Console.ResetColor();
    return 0;
}

// 5. Aggregate inventory data
foreach (var item in analyzableItems)
{
    var matchingSlots = slots.Where(s => s.ItemId == item.Id).ToList();
    item.TotalCount = matchingSlots.Sum(s => s.Count);
    item.FoundOnCharacters = matchingSlots.Select(s => s.CharacterName).Distinct().ToList();
}

// 6. Check recipes
Console.Write($"Checking recipes for {analyzableItems.Count} items (may take a while)... ");
var trophyIds = analyzableItems.Select(i => i.Id).ToList();
await recipeCheckService.BulkCheckRecipesAsync(trophyIds);

foreach (var item in analyzableItems)
{
    var (usedInRecipes, recipeCount) = await recipeCheckService.CheckItemRecipesAsync(item.Id);
    item.UsedInRecipes = usedInRecipes;
    item.RecipeCount = recipeCount;
}
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("OK");
Console.ResetColor();
Console.WriteLine();

// 7. Check collections
Console.Write("Loading collection data (achievements)... ");
try
{
    await collectionService.InitializeAsync();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("OK");
    Console.ResetColor();

    var collectionMap = collectionService.GetCollectionMap();
    foreach (var item in analyzableItems)
        CollectionMapper.ApplyCollectionInfo(item, collectionMap);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"Failed (continuing without collection data): {ex.Message}");
    Console.ResetColor();
}
Console.WriteLine();

// 8. Classify
foreach (var item in analyzableItems)
    classifier.Classify(item);

// 9. Display results
var grouped = analyzableItems
    .OrderBy(i => i.Safety)
    .ThenBy(i => i.Rarity)
    .ThenBy(i => i.Name)
    .GroupBy(i => i.Safety);

var stats = new Dictionary<ItemSafety, int>
{
    { ItemSafety.Safe, 0 },
    { ItemSafety.Check, 0 },
    { ItemSafety.Keep, 0 }
};

foreach (var group in grouped)
{
    var (color, icon, label) = group.Key switch
    {
        ItemSafety.Safe => (ConsoleColor.Green, "🟢", "SAFE TO DESTROY"),
        ItemSafety.Check => (ConsoleColor.Yellow, "🟡", "CHECK BEFORE DESTROYING"),
        ItemSafety.Keep => (ConsoleColor.Red, "🔴", "DO NOT DESTROY"),
        _ => (ConsoleColor.White, "⚪", "UNKNOWN")
    };

    Console.ForegroundColor = color;
    Console.WriteLine($"━━━ {icon} {label} ({group.Count()}) ━━━");
    Console.ResetColor();

    foreach (var item in group)
    {
        stats[group.Key]++;

        Console.ForegroundColor = color;
        Console.Write($"  {icon} ");
        Console.ResetColor();
        Console.Write($"{item.Name}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" ({item.Type}, {item.Rarity}");
        if (item.TotalCount > 1) Console.Write($", x{item.TotalCount}");
        if (item.VendorValue > 0) Console.Write($", sell: {ItemClassifier.FormatCoins(item.VendorValue)}");
        Console.WriteLine(")");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"     Reason: {item.SafetyReason}");

        if (item.FoundOnCharacters.Count > 0)
            Console.WriteLine($"     Found on: {string.Join(", ", item.FoundOnCharacters)}");

        if (item.Flags.Count > 0)
            Console.WriteLine($"     Flags: {string.Join(", ", item.Flags)}");

        if (item.UsedInRecipes)
            Console.WriteLine($"     Recipes: used in {item.RecipeCount} recipe(s)");

        if (item.BelongsToCollection)
        {
            var colStatus = item.AllCollectionsCompleted ? "completed" : "incomplete";
            Console.WriteLine($"     Collection: {string.Join(", ", item.CollectionNames)} ({colStatus})");
            if (item.CollectionProgress != null && !item.AllCollectionsCompleted)
                Console.WriteLine($"     Progress: {item.CollectionProgress}");
        }

        Console.ResetColor();
        Console.WriteLine();
    }
}

// 10. Summary
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("━━━ SUMMARY ━━━");
Console.ResetColor();
Console.WriteLine($"  Total items analyzed: {analyzableItems.Count}");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"  🟢 Safe to destroy:    {stats[ItemSafety.Safe]}");
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"  🟡 Check first:        {stats[ItemSafety.Check]}");
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine($"  🔴 Do not destroy:     {stats[ItemSafety.Keep]}");
Console.ResetColor();

// 11. Export JSON for validation
var jsonOutput = analyzableItems
    .OrderBy(i => i.Safety)
    .ThenBy(i => i.Name)
    .Select(i => new
    {
        i.Id,
        i.Name,
        i.Type,
        i.Rarity,
        Safety = i.Safety.ToString(),
        i.SafetyReason,
        i.TotalCount,
        i.VendorValue,
        i.UsedInRecipes,
        i.RecipeCount,
        i.BelongsToCollection,
        CollectionNames = i.CollectionNames.Count > 0 ? i.CollectionNames : null,
        i.AllCollectionsCompleted,
        i.FoundOnCharacters,
    });

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
var json = JsonSerializer.Serialize(jsonOutput, jsonOptions);
var jsonPath = Path.Combine(AppContext.BaseDirectory, "analysis-results.json");
File.WriteAllText(jsonPath, json);
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"JSON results exported to: {jsonPath}");
Console.ResetColor();

return 0;
