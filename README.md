# DesChoyable — GW2 Inventory Analyzer

A Choya-approved tool for Guild Wars 2 that analyzes your inventory and classifies **Trophy** and **Gizmo** items into three categories:

- **Safe to Destroy** Vendor junk, low-value trophies, completed collection items
- **Check Before Destroying** Mid-value items that may or may not be useful
- **Do Not Destroy** Items used in recipes, incomplete collections, quest items, valuable rarities

## Project Structure

```
DestroyChecker.sln
├── src/DestroyChecker.Core/           (netstandard2.0) — Shared classification logic
├── src/DestroyChecker.ConsoleTest/    (net8.0)         — Console app for testing
└── src/DestroyChecker.BlishModule/    (net48)          — Blish HUD overlay module
```

### Core Library

Contains all classification logic with no HTTP dependencies:

- **ItemClassifier** 14-rule engine that evaluates items based on rarity, vendor value, flags, recipes, and collections
- **CollectionMapper** Maps items to their achievement collections and completion status
- **Models** `ItemInfo`, `ItemSafety`, `InventorySlot`, `CollectionEntry`, `AchievementData`

### Console Test App

Standalone CLI tool for development and testing. Uses `HttpClient` to call the GW2 API directly.

### Blish HUD Module

Overlay module that runs inside [Blish HUD](https://blishhud.com/). Features:

- Scans only the **current in-game character** (via Mumble)
- Auto-rescans on character switch or every 30 seconds
- Collapsible color-coded sections (Safe / Check / Keep)
- Corner icon to toggle the analysis window
- Uses Blish HUD's secure API subtoken system

## Getting Started

### Console App

```bash
export GW2_API_KEY="your-api-key-here"
cd src/DestroyChecker.ConsoleTest
dotnet run
```

Required API key permissions: `account`, `characters`, `inventories`, `progression`.

### Blish HUD Module (Linux + Proton)

This setup uses **Gw2-Simple-Addon-Loader** to inject Blish HUD inside GW2 running on Steam via Proton.

**Prerequisites:**
- GW2 installed via Steam with Proton
- [Gw2-Simple-Addon-Loader](https://github.com/) configured in `addons/LOADER_public/`
- `flatpak` with `protontricks` installed
- Steam launch command set in GW2 properties:
  ```
  "<SteamLibrary>/steamapps/common/Guild Wars 2/start_blish.sh" %command%
  ```

**Build & Deploy:**

```bash
# Set your Blish HUD modules folder (or export in your .bashrc):
export BLISH_MODULES_DIR="<SteamLibrary>/steamapps/compatdata/1284210/pfx/drive_c/users/steamuser/Documents/Guild Wars 2/addons/blishhud/modules"

# One-command build + deploy:
chmod +x deploy.sh
./deploy.sh

# Or manually:
dotnet build
cp src/DestroyChecker.BlishModule/bin/Debug/net48/DestroyChecker.BlishModule.bhm \
  "<SteamLibrary>/steamapps/compatdata/1284210/pfx/drive_c/users/steamuser/Documents/Guild Wars 2/addons/blishhud/modules/"
```

**Module location (Linux/Proton):**
```
<SteamLibrary>/steamapps/compatdata/1284210/pfx/drive_c/users/steamuser/Documents/Guild Wars 2/addons/blishhud/modules/
```

**Testing:**
1. Run `./deploy.sh` (builds and copies `.bhm` to modules folder)
2. Launch GW2 via Steam (the `start_blish.sh` will start Blish HUD with the game)
3. In-game, click the DesChoyable corner icon (top-left, near inventory icon)
4. The module auto-scans the current character's inventory on load and every 30 seconds

**Windows:**
```
%userprofile%\Documents\Guild Wars 2\addons\blishhud\modules\
```
Copy `DestroyChecker.BlishModule.bhm` there and enable the module in Blish HUD settings.

## Classification Rules

| Rule | Safety | Description |
|------|--------|-------------|
| Quest Item | Keep | Known story/quest items |
| DeleteWarning flag | Keep | Items flagged by the game as important |
| Used in recipes | Keep | Crafting ingredient |
| Incomplete collection | Keep | Needed for an unfinished achievement |
| Completed collection (high rarity) | Check | Collection done but item is Exotic+ |
| Completed collection (low rarity) | Safe | Collection done and item is common |
| Exotic+ rarity | Keep | Exotic, Ascended, or Legendary items |
| High vendor value (≥1g) | Check | Worth selling to vendor |
| Rare rarity | Check | May have Trading Post value |
| AccountBound + no recipes + no collection | Safe | Untradeable with no known use |
| Low vendor value (≤50c) | Safe | Vendor junk |
| Fine rarity + low value | Safe | Common trophies |
| Moderate vendor value | Check | May be worth investigating |
| Default | Check | Anything not matched above |

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (for building)
- [Blish HUD](https://blishhud.com/) 1.2.0+ (for the overlay module)
- GW2 API key with permissions: `account`, `characters`, `inventories`, `progression`

## License

The Fighters of Zelda provideds this project as-is for anyone that want to use, or imporve the experience with Guild Wars 2. 
