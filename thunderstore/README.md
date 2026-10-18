# Stonewards Ore Auto Pickup

**Automatically collect selected mined resources without stopping to pick up every piece of ore.** Choose exactly what to collect with a compact in-game menu.

## ✨ Features

- **Selective ore pickup** — collect nearby resources automatically while leaving unwanted materials on the ground.
- **Per-resource filter** — configure individual ores with in-game icons, search, and quick enable/disable controls.
- **Stone off by default** — ignore Stone on the first two maps; other discovered resources are enabled.
- **Colored pickup notifications** — see how much ore was actually added to your inventory.
- **Convenient settings** — customize pickup range and the menu shortcut.
- **Own-drop protection** — attempts to leave ore you deliberately dropped on the ground.

## 🎮 How to use

Press **F10** during a game to open the settings menu. Select **CONFIGURE ORES** to choose which resources are collected automatically. The selection is saved to your active mod-manager profile.

## ⚙️ Configuration

- **Automatic pickup:** enabled by default.
- **Pickup radius:** 2.5 m by default; adjustable from 1 to 4 m.
- **Stone:** disabled by default; can be enabled per resource.
- **Other discovered ores:** enabled by default.
- **Shortcut:** F10, rebindable.

## 📦 Installation

Install **OreAutoPickup** using **r2modman** or another compatible Thunderstore mod manager, then launch Stonewards through the mod manager. The package declares a BepInEx 5 dependency.

## 🔧 Compatibility

- **Game:** Stonewards; resource IDs were checked against game version 0.2.3.
- **Mod loader:** BepInEx 5.
- **Multiplayer:** Install locally for each player who wants the feature. Multiplayer drop protection has not been fully verified.

## ⚠️ Known limitations

- Ore dropped by **other players** can still be auto-collected if it appears as an ordinary world pickup. Suppression of your own dropped stacks is best-effort.
- Collection still follows the game's normal pickup restrictions. Newly loaded resources appear when their game data becomes available.

## 🔗 Links

[GitHub](https://github.com/thereisnouser/stonewards-ore-auto-pickup) · [Report an issue](https://github.com/thereisnouser/stonewards-ore-auto-pickup/issues) · [Changelog](https://github.com/thereisnouser/stonewards-ore-auto-pickup/blob/main/thunderstore/CHANGELOG.md)

---

*Made by thereisnouser*
