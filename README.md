# Stonewards Ore Auto Pickup

A **client-side BepInEx 5** mod for Stonewards that automatically picks up selected, nearby *mined resources* using Stonewards' own `CmdAutoPickupItem` network command.

## Features

- Open a compact Stonewards-inspired in-game settings screen with **F10**.
- Dynamically discovers actual ore/resource item IDs from loaded `DigMatterProfileSO.resourceClusters` data. No hard-coded, potentially fictitious ore names.
- Toggle each mined resource individually, or enable/disable all.
- Configure collection radius (1.0–4.0 meters) and rebind the settings shortcut.
- Saves settings in the active profile at `BepInEx/config/community.stonewards.oreautopickup.cfg`.
- Uses the existing networked pickup command. It does **not** mint inventory items, destroy pickups locally, change saves, or scoop up teammates' items from arbitrary distances.

## Build and install

Requires the .NET SDK, Stonewards game files and BepInEx 5 in your active profile.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1 `
  -GameDir "F:\Steam\steamapps\common\Stonewards" `
  -ProfileDir "C:\Users\user\AppData\Roaming\r2modmanPlus-local\Stonewards\profiles\sw-test-bell-control"
```

When no `-ProfileDir` is supplied, the plugin is installed into the game directory (provided BepInEx is present there).

Launch the profile via **Start modded**, load into a playable level and press **F10**. The ore list populates from the game's loaded level data. The mod avoids attempting pickups when the inventory or native menus are open, the player is dead, or networking is unavailable.

## Status

**Source prototype (v0.1.0)**. The source is based on independently observed Stonewards 0.2.x method signatures, but has **not been compiled against a full local game install or runtime-tested** here. Verify it on a separate test profile before using it on your main save. Please share build logs and observed pickup behavior to finalize compatibility.

## Technical choices

Ore discovery reads `DigMatterProfileSO.resourceClusters` (the resources mining can actually produce), and filters out scrap items. The plugin then calls `FirstPersonController.CmdAutoPickupItem(player, pickable)` only for selected `PickableItem` objects that are currently in range. It never calls `CmdForcePickupItem`, which would bypass normal world-pickup flow.

See `docs/TEST_PLAN.md` for checks before a public release.
