# OreAutoPickup 0.1.0 — release checklist

This checklist is for the **first public Thunderstore release**. Keep the published version at `0.1.0`; internal prototype version numbers are not public release numbers.

## Before packaging

- [ ] Build the current `main` branch using `build-and-install.ps1` against your installed Stonewards game and the `stonewards-ore-auto-pickup` r2modman profile. Confirm **zero compiler errors**.
- [ ] Use `docs/TEST_PLAN.md` to verify F10, resource selection, defaults, and correct confirmed inventory-count notifications on the two maps.
- [ ] Check the latest own-drop protection in actual gameplay. If it fails, document the behavior and consider postponing that feature; do **not** claim protection of teammates' thrown ore.
- [ ] Confirm the plugin log reports `Ore Auto Pickup v0.1.0 initialized`.
- [ ] Add a final **256 × 256 PNG** icon to `thunderstore/icon.png`. The package script refuses to proceed without it.
- [ ] Confirm that `thunderstore/README.md`, `thunderstore/CHANGELOG.md`, `thunderstore/manifest.json`, and the source code describe the same functionality.

## Build the Thunderstore ZIP

Run from the repository root in Windows PowerShell:

```powershell
$GameDir = "PATH_TO_STONEWARDS"
$ProfileDir = Join-Path $env:APPDATA "r2modmanPlus-local\Stonewards\profiles\stonewards-ore-auto-pickup"

.\package-thunderstore.ps1 -GameDir $GameDir -ProfileDir $ProfileDir
```

The expected output is `dist/OreAutoPickup-0.1.0.zip`. The script checks the 0.1.0 versions, icon dimensions, DLL assembly metadata, compilation, and ZIP content.

The ZIP contains:

```text
manifest.json
README.md
CHANGELOG.md
icon.png
plugins/StonewardsOreAutoPickup.dll
```

The packaged `README.md` must come from **`thunderstore/README.md`**, not the root GitHub README.

## Publish

- [ ] Upload the ZIP to the **Stonewards** Thunderstore community under the verified author namespace.
- [ ] Confirm the package name is `OreAutoPickup` and the first published version is `0.1.0`.
- [ ] Confirm the page description, icon and dependency display correctly.
- [ ] Copy the actual Thunderstore package URL (do not guess the slug), then add it to both GitHub and Thunderstore descriptions if desired.
- [ ] Tag the verified release commit as `v0.1.0` once publication is confirmed.

## Suggested GitHub repository description

Automatically pick up selected Stonewards ores with an in-game resource filter, item icons, and accurate colored pickup notifications.
