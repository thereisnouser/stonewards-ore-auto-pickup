# Stonewards Ore Auto Pickup

**Automatically collect selected mined resources without stopping to pick up every piece of ore.** Choose exactly what to collect with a compact in-game menu.

## ✨ Features

- **Selective collection** — automatically pick up nearby mined resources you enable; leave unwanted materials on the ground.
- **Resource-specific settings** — choose ores individually in a separate F10 window with their in-game icons, search, and Enable All / Disable All controls.
- **Stone excluded by default** — both known Stone item IDs start disabled; other discovered resources start enabled. Your choices are saved.
- **Readable pickup feedback** — displays the actual increase in your inventory with colored text and the game's UI font.
- **Adjustable controls** — set the collection radius (1–4 m, default 2.5 m) and rebind the settings shortcut.
- **Own-drop protection** — tries to avoid automatically collecting stacks you deliberately drop from your inventory.

## 🎮 How to use

1. Start Stonewards with your modded profile and join a game.
2. Mining nearby selected resources automatically sends the normal in-game pickup request.
3. Press **F10** to open the settings menu, then select **CONFIGURE ORES** to choose which resources to collect.
4. To collect a disabled resource, pick it up manually or enable it in the filter.

The menu shortcut can be changed in settings. Existing resource selections are stored in your active profile.

## ⚙️ Configuration

Settings are stored in `BepInEx/config/community.stonewards.oreautopickup.cfg` within the active r2modman profile.

| Option | Default | Effect |
| --- | --- | --- |
| Enabled | On | Master toggle for automatic collection |
| PickupRadius | 2.5 m | Collection radius, adjustable from 1 to 4 m |
| ToggleSettings | F10 | In-game configuration shortcut |
| `STONE`, `STONE_MAP_2` | Off | Ignore Stone on the first two maps |
| Other discovered ores | On | Collect automatically unless disabled |
| UseManualPickupRoute | On | Uses the game's regular pickup command |

Existing profiles upgrading from earlier development builds disable Stone once per known Stone ID. Subsequent changes made in F10 remain saved.

## 📦 Installation

### Mod manager

After the package is published, install **OreAutoPickup** with **r2modman** or another compatible Thunderstore manager and launch the game using that profile.

Until then, build from source using the instructions below.

## 🔧 Compatibility

- **Game:** Stonewards; mined-resource IDs and icons were checked against player logs from game version 0.2.3. Other updates are not verified.
- **Mod loader:** BepInEx 5.
- **Multiplayer:** Each player who wants automatic collection installs the mod locally. Pickup requests use Stonewards' existing network commands. Cooperative drop handling has not been fully verified.

## ⚠️ Known limitations

- **Other players' dropped ore** may be collected automatically when it appears as an ordinary world pickup. Protection for your **own** dropped stacks is best-effort and still needs testing after the latest changes.
- The mod does not add resources to the inventory directly; pickups remain subject to the game's normal permissions and inventory limits.
- Ores available in the selection menu depend on which game resource profiles have loaded.

## 🛠️ Development

### Requirements

- .NET SDK with `netstandard2.1` support.
- Stonewards installed locally.
- BepInEx 5 available in the game installation or the active mod-manager profile.
- Windows PowerShell.

### Building

From the cloned repository:

```powershell
$GameDir = "PATH_TO_STONEWARDS"
$ProfileDir = Join-Path $env:APPDATA "r2modmanPlus-local\Stonewards\profiles\stonewards-ore-auto-pickup"

.\build-and-install.ps1 -GameDir $GameDir -ProfileDir $ProfileDir
```

This builds the project and installs the plugin DLL in that profile.

### Release packaging

Provide a **256 × 256 PNG** at `thunderstore/icon.png`, then run:

```powershell
.\package-thunderstore.ps1 -GameDir $GameDir -ProfileDir $ProfileDir
```

The packager verifies version consistency, compiles the DLL and checks the Thunderstore ZIP layout before creating `dist/OreAutoPickup-0.1.0.zip`. It deliberately fails if the icon or any required asset is missing.

### Implementation

The plugin discovers actual resource types through Stonewards' loaded item and mining profile data. It requests normal networked pickup of supported world items, checks local inventory changes for notifications, and uses a Harmony hook for best-effort identification of locally dropped stacks.

## 🔗 Links

[Report an issue](https://github.com/thereisnouser/stonewards-ore-auto-pickup/issues) · [Changelog](thunderstore/CHANGELOG.md)

---

*Made by thereisnouser*
