using System.Net.Http.Json;
using DestroyChecker.Core.Models;

namespace DestroyChecker.ConsoleTest.Services;

public class InventoryService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public InventoryService(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public async Task<List<InventorySlot>> GetAllInventorySlotsAsync()
    {
        var slots = new List<InventorySlot>();

        var shared = await GetSharedInventoryAsync();
        slots.AddRange(shared);

        var characters = await GetCharacterNamesAsync();
        foreach (var name in characters)
        {
            var charSlots = await GetCharacterInventoryAsync(name);
            slots.AddRange(charSlots);
        }

        return slots;
    }

    public async Task<List<InventorySlot>> GetSingleCharacterSlotsAsync(string characterName)
    {
        var slots = new List<InventorySlot>();

        var shared = await GetSharedInventoryAsync();
        slots.AddRange(shared);

        var charSlots = await GetCharacterInventoryAsync(characterName);
        slots.AddRange(charSlots);

        return slots;
    }

    public async Task<List<string>> GetCharacterNamesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.guildwars2.com/v2/characters");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var names = await response.Content.ReadFromJsonAsync<List<string>>();
        return names ?? new List<string>();
    }

    private async Task<List<InventorySlot>> GetSharedInventoryAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.guildwars2.com/v2/account/inventory");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<ApiInventorySlot?>>();

        return (items ?? new List<ApiInventorySlot?>())
            .Where(i => i is not null)
            .Select(i => new InventorySlot
            {
                ItemId = i!.Id,
                Count = i.Count,
                IsSharedInventory = true,
                CharacterName = "Shared Inventory"
            })
            .ToList();
    }

    public async Task<List<InventorySlot>> GetCharacterInventoryAsync(string characterName)
    {
        var slots = new List<InventorySlot>();
        var encodedName = Uri.EscapeDataString(characterName);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.guildwars2.com/v2/characters/{encodedName}/inventory");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        ApiCharacterInventory? inventory;
        try
        {
            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            inventory = await response.Content.ReadFromJsonAsync<ApiCharacterInventory>();
        }
        catch (HttpRequestException)
        {
            return slots;
        }

        if (inventory?.Bags is null) return slots;

        foreach (var bag in inventory.Bags)
        {
            if (bag?.Inventory is null) continue;
            foreach (var item in bag.Inventory)
            {
                if (item is null) continue;
                slots.Add(new InventorySlot
                {
                    ItemId = item.Id,
                    Count = item.Count,
                    CharacterName = characterName,
                    IsSharedInventory = false
                });
            }
        }

        return slots;
    }

    // API DTOs
    private record ApiInventorySlot(int Id, int Count);
    private record ApiCharacterInventory(List<ApiBag?> Bags);
    private record ApiBag(List<ApiBagItem?> Inventory);
    private record ApiBagItem(int Id, int Count);
}
