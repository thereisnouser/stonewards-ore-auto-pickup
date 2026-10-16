# Test plan — Ore Auto Pickup 0.1.0

1. Launch a separate modded test profile. Verify the `Ore Auto Pickup v0.1.0 initialized.` line in `BepInEx/LogOutput.log`.
2. Open settings with F10 in a playable level, check cursor unlock, and close with F10/X/Esc. Confirm movement and camera controls restore normally.
3. Mine two different resource types; ensure each ID appears at most once in the list, with a localized label or stable ID.
4. Turn all types OFF. Mine and approach drops: nothing should be automatically collected, but vanilla manual pickup should still work.
5. Turn just one type ON, keeping another OFF. Approach both. Only the selected type should enter inventory, with the correct amount and no duplication on the ground.
6. Fill inventory: failed pickups should remain on the ground, not disappear or duplicate; disable or clear inventory and retry.
7. Test as lobby host and as a regular client. Verify pickup does not take another player's ore outside the chosen radius.
8. Test opening a native menu or respawning while the settings menu is active; no stuck cursor or input state.
9. Restart and confirm selections, pickup radius, and customized F10 shortcut persist.
10. Inspect log for `Pickup request failed` or `Ore scan failed` warnings. Do not publish the mod until gameplay behavior has been verified.
