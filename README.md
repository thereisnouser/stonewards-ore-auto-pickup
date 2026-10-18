# Stonewards Ore Auto Pickup

A lightweight **client-side BepInEx 5** mod for Stonewards. Automatically collects the mined resources you select, without creating inventory items out of thin air.

## Features (development 0.2.5)

- Press **F10** to open the main settings window.
- Choose **CONFIGURE ORES** to open a separate resource selection window.
- Each resource displays the game's **actual item icon**, localized name (when available), a material-colored label, and an individual **PICK UP / IGNORE** toggle.
- Search resources by name or item ID, plus **ENABLE ALL / DISABLE ALL** actions.
- Uses Stonewards' own `Jersey10-Regular` UI font when loaded, falling back to `Jersey10-Regular2`.
- Adjust auto-pickup radius from **1.0 m to 4.0 m** and rebind F10.
- Selection and key preferences persist in `BepInEx/config/community.stonewards.oreautopickup.cfg`.
- Only automatically collects unowned mined-resource world pickups; leaves dropped player items and other loot alone.

## Real resource IDs and readable colors (0.2.4)

Ore discovery and selection are always dynamic: the source of truth is the game's own `ItemDataSO` referenced in `DigMatterProfileSO.resourceClusters`. Only material colors are explicitly mapped, using **nine exact IDs observed in a player's Stonewards 0.2.3 BepInEx log** across the first two maps:

| In-game item ID | In-game name | Color |
|---|---|---|
| `STONE` | Stone | Warm gray `#BEBEB2` |
| `IRON` | Iron | Steel blue-gray `#AAC2CE` |
| `DIAMOND` | Diamond | Icy cyan `#7BDFF1` |
| `GOLD` | Gold | Gold `#FFCD56` |
| `EMERALD` | Emerald | Green `#57DB80` |
| `STONE_MAP_2` | Stone | Warm gray `#BEBEB2` |
| `IRON_MAP_2` | Copper | Copper orange `#EB8E56` |
| `GOLD_MAP_2` | Platinum | Cool platinum `#D8E1EC` |
| `EMERALD_MAP_2` | Ruby | Ruby red `#F45D73` |

**Technical IDs are not material names.** For instance `IRON_MAP_2` is Copper, not Iron. The colors above are carefully chosen approximations for clear HUD text, **not measured RGB values from game icon textures**.

Unknown/future resource IDs are still supported: use readable icon-derived tints when possible, otherwise stable per-ID colors. No assumed resources are added to the filter.

To check what your particular game has loaded, search `BepInEx/LogOutput.log` in the active profile for `Discovered real mined resource`. Resource logs include ID, localized name, sprite name, rarity and assigned tint.

## Dropping ore without collecting it again (0.2.5)

**Ore you intentionally drop from your inventory is no longer a valid auto-pickup target.** A small Harmony hook watches the game's own `FirstPersonController.CmdReleaseItem` command for the local player. It records the specific resource and drop position, then matches the newly spawned world pickup against objects that were already on the ground. The resulting pickup is excluded from this mod's auto-pickup until it despawns.

- Fresh ore mined nearby remains eligible for automatic collection.
- Manual pickup still works when you choose to retrieve your dropped ore.
- A temporary exclusion zone around the drop position guards against delayed network spawning. If the pickup cannot be uniquely identified, that fallback remains while you stay near the drop, and clears when you move away.
- Protection is client-side. It does **not** change host or teammate behavior, world drops, inventory numbers, or save data.
- Logging includes `Own-drop guard enabled`, `Ore dropped manually`, and `Excluding own dropped` to help diagnose a missed drop.

Requires BepInEx's bundled `0Harmony.dll` (already provided by typical BepInEx 5 installs). This behavior is not yet runtime-tested on the current game version; see the test plan.

## Pickup notifications and inventory quantities

**0.2.1:** Restores the on-screen `+N IRON`-style notification that disappeared when the pickup command changed in 0.2.0. The new notification uses the game's `Jersey10-Regular` font, approximate resource-specific colors, and the **confirmed inventory count delta** rather than the ground-stack quantity or amount requested.

The mod snapshots `InventorySystem.GetItemCount(itemID)` before requesting a pickup, waits for the networked inventory to increase, then displays the actual increment. If the inventory does not increase, **no notification is shown**. Repeated nearby collections of the same ore are merged into a single floating message. This logic uses no forged inventory changes.

The manual pickup route (`CmdPickupItem`) remains the default. If it does not work on a particular client or server, `[Advanced] UseManualPickupRoute = false` restores the previous auto-pickup route; note that its original in-game notification may then also appear.

**This behavior requires in-game verification.** We have not yet validated the exact inventory sync timing or compile/runtime compatibility after these changes.

## Build & install

Requires a local **.NET SDK**, Stonewards game assemblies and BepInEx 5 installed in the matching r2modman profile.

Run the following from the cloned repository in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1 `
  -GameDir "F:\Steam\steamapps\common\Stonewards" `
  -ProfileDir "C:\Users\user\AppData\Roaming\r2modmanPlus-local\Stonewards\profiles\stonewards-ore-auto-pickup"
```

Launch **Start modded** using the same profile. The resource list is loaded dynamically from `DigMatterProfileSO.resourceClusters` when the game's level content is available. Open **F10 → CONFIGURE ORES** to edit selection. You can also edit the configuration using r2modman's Config editor.

## Current status

**0.2.5 development build**, not a Thunderstore release. Updated source has not been compiled against the user's installed game or tested in multiplayer from this environment. See [the test plan](docs/TEST_PLAN.md) for the quantity, UI, icon and co-op checks required before a public release.

## Feedback

Please include the game version, host/client role, **on-screen pickup number vs actual inventory increase**, and the active profile's `BepInEx/LogOutput.log` if reporting a bug.
