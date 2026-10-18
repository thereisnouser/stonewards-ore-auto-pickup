# QA checklist — Ore Auto Pickup 0.2.5 (development)

Test only with the r2modman profile **stonewards-ore-auto-pickup** and keep the game log.

## Build and UI

1. Build the source using `build-and-install.ps1`; confirm **0 errors**, and SHA256 matches the installed DLL.
2. Launch via **Start modded**; log must contain `Ore Auto Pickup v0.2.5 initialized`.
3. Press **F10**: check the main window at 1920×1080 and 2560×1440. Check that the game stops character input and restores cursor controls when closing.
4. Press **CONFIGURE ORES**. Verify it opens a **separate window** with real item icons, tinted names, search, and individual toggles.
5. Confirm `Jersey10-Regular` (or fallback `Jersey10-Regular2`) appears in the active log after opening settings.
6. On map 1 verify Stone #BEBEB2, Iron #AAC2CE, Diamond #7BDFF1, Gold #FFCD56 and Emerald #57DB80. On map 2 verify Stone #BEBEB2, Copper (`IRON_MAP_2`) #EB8E56, Platinum (`GOLD_MAP_2`) #D8E1EC and Ruby (`EMERALD_MAP_2`) #F45D73. Match the colors of the selection window and the pickup popup. These are chosen approximations, not sampled icon RGB values. Confirm the `Discovered real mined resource: ID=...; Name=...; Icon=...; Rarity=...; Tint=#...` log lines.
7. Check **ENABLE ALL**, **DISABLE ALL**, searching, scrolling and returning to the main window. Restart to verify selection persists.

## Correctness / quantities (highest priority)

8. With one enabled resource and all others disabled, mine the selected one. Record **before/after inventory count**, **number on the pickup notification**, and **amount shown in the world stack if visible**.
9. Verify the center-screen colored **+N ORE** popup is visible again. It must use the game's Jersey10 font, show the selected ore name and match the exact inventory delta. If no ore was added (inventory full), there must be no popup. Repeat for stacks of multiple units, partial inventory stacks, and cases with resource bonuses / mining effects. Notification increase must match actual inventory increase.
10. Collect two world stacks of the same material in quick succession; the toast should merge confirmed additions without double counting.
11. Mine a disabled resource and verify it remains on the ground; manual pickup must still work.
12. Fill inventory and walk into selected drops. No world object should disappear without corresponding inventory space, and no repeat-pickup duplication should occur.
13. Test standing near ore over 20 seconds to confirm one item is never credited repeatedly.
14. Repeat as **host and non-host client**. With teammates nearby, make sure no dropped teammate-owned item is taken automatically.
15. If manual route fails in your session, change `[Advanced] UseManualPickupRoute` to `false` and compare. Include logs and measured deltas when reporting; do not release publicly with an unconfirmed count discrepancy.

## Dropped ore guard (0.2.5)

16. In the active modded profile, check `BepInEx/LogOutput.log` includes `Own-drop guard enabled.` when joining the game. A Harmony hook failure must not prevent inventory interaction.
17. Mine some Iron, open the inventory, drop one stack at your feet, and close the inventory. Verify the quantity actually leaves the inventory and **does not return automatically**, even after waiting 20 seconds.
18. Move away from the dropped stack and return. The **same world stack** should still be excluded from automatic pickup.
19. Mine **new** Iron beside that dropped stack: the newly mined ore should still be automatically collected while the manually dropped stack remains on the ground. Repeat with Gold and a second-map resource.
20. Manually collect the dropped stack using the normal game interaction. Verify it enters the inventory exactly once and the mod does not duplicate it.
21. Drop two stacks rapidly (of the same and then different resource IDs), and repeat tests as host and normal client. Check no player-owned pickup is accidentally marked as ore mined by somebody else.
22. If the drop is still immediately re-collected, send the `Ore dropped manually:` and `Excluding own dropped` log lines plus a screenshot/video. This distinguishes a missing command hook from a network spawn identification mismatch.

## Log diagnostics

Look for `Pickup request:`, `Ore discovery failed:`, `Pickup scan failed:`, and `native UI font:`. Debug log lines may require a verbose BepInEx log level.
