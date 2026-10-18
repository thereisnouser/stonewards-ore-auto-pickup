using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StonewardsOreAutoPickup
{
    // Protect individual world stacks created by the local player's deliberate
    // inventory drop. This does not change ordinary pickup or inventory code.
    internal static class OwnDroppedOre
    {
        private const float IdentificationWindow = 12f;
        private const float MatchRadius = 2.8f;
        private const float FallbackRadius = 3.0f;
        private const float LeaveDistance = 5f;

        private sealed class DropRequest
        {
            internal string Id;
            internal Vector3 Position;
            internal float Deadline;
            internal readonly HashSet<int> Existing = new HashSet<int>();
        }

        private static readonly List<DropRequest> Pending = new List<DropRequest>();
        private static readonly Dictionary<int, PickableItem> Ignored =
            new Dictionary<int, PickableItem>();
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static float _nextPrune;
        internal static Func<string, bool> IsOreId;

        internal static void Install(ManualLogSource log)
        {
            _log = log;
            try
            {
                var target = AccessTools.Method(typeof(FirstPersonController), "CmdReleaseItem");
                if (target == null)
                {
                    log.LogWarning("Own-drop guard disabled: CmdReleaseItem method not found.");
                    return;
                }

                var prefix = new HarmonyMethod(typeof(OwnDroppedOre).GetMethod(
                    "BeforeItemRelease", BindingFlags.Static | BindingFlags.NonPublic));
                _harmony = new Harmony("community.stonewards.oreautopickup.dropguard");
                _harmony.Patch(target, prefix: prefix);
                log.LogInfo("Own-drop guard enabled.");
            }
            catch (Exception e)
            {
                log.LogWarning("Own-drop guard could not hook inventory release: " + e.Message);
            }
        }

        // The Stonewards Mirror command receives:
        // (FirstPersonController player, string itemId, int count, int slot, Vector3 position)
        private static void BeforeItemRelease(FirstPersonController __instance, object[] __args)
        {
            if (__instance == null || !__instance.isLocalPlayer ||
                __args == null || __args.Length < 5 ||
                !ReferenceEquals(__args[0], __instance))
                return;

            var id = __args[1] as string;
            if (string.IsNullOrEmpty(id) || !( __args[4] is Vector3) ||
                IsOreId == null || !IsOreId(id))
                return;

            var position = (Vector3)__args[4];
            var request = new DropRequest
            {
                Id = id,
                Position = position,
                Deadline = Time.unscaledTime + IdentificationWindow
            };

            // Snapshot existing pickups to avoid mistaking ore already on the
            // ground for the newly dropped inventory stack.
            foreach (var pickable in UnityEngine.Object.FindObjectsOfType<PickableItem>())
                if (pickable != null)
                    request.Existing.Add(pickable.GetInstanceID());

            Pending.Add(request);
            _log?.LogInfo("Ore dropped manually: " + id + " (auto pickup protected).");
        }

        internal static void Observe(PickableItem[] pickups, Vector3 playerPosition)
        {
            float now = Time.unscaledTime;
            float radius2 = MatchRadius * MatchRadius;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var request = Pending[i];
                if (now < request.Deadline)
                {
                    PickableItem match = null;
                    float distance = radius2;
                    foreach (var pickup in pickups)
                    {
                        if (pickup == null || !pickup.isActiveAndEnabled ||
                            request.Existing.Contains(pickup.GetInstanceID()))
                            continue;

                        var item = pickup.ItemDataSO;
                        if (item == null || item.itemID != request.Id)
                            continue;

                        float d = (pickup.transform.position - request.Position).sqrMagnitude;
                        if (d < distance)
                        {
                            match = pickup;
                            distance = d;
                        }
                    }
                    if (match != null)
                    {
                        Ignored[match.GetInstanceID()] = match;
                        Pending.RemoveAt(i);
                        _log?.LogInfo("Excluding own dropped " + request.Id +
                            " from auto pickup, object " + match.GetInstanceID());
                        continue;
                    }
                }

                // If network creation cannot be matched, the original drop
                // position remains protected until the player leaves it.
                if (now >= request.Deadline &&
                    (playerPosition - request.Position).sqrMagnitude > LeaveDistance * LeaveDistance)
                    Pending.RemoveAt(i);
            }

            if (now >= _nextPrune)
            {
                _nextPrune = now + 10f;
                var gone = new List<int>();
                foreach (var entry in Ignored)
                    if (entry.Value == null)
                        gone.Add(entry.Key);
                foreach (var id in gone)
                    Ignored.Remove(id);
            }
        }

        internal static bool ShouldIgnore(PickableItem item, string id)
        {
            if (Ignored.TryGetValue(item.GetInstanceID(), out var previouslyDropped) &&
                previouslyDropped == item)
                return true;

            foreach (var request in Pending)
                if (request.Id == id &&
                    (item.transform.position - request.Position).sqrMagnitude <=
                    FallbackRadius * FallbackRadius)
                    return true;
            return false;
        }

        internal static void Clear()
        {
            Pending.Clear();
            Ignored.Clear();
        }

        internal static void Uninstall()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            IsOreId = null;
            Clear();
        }
    }
}
