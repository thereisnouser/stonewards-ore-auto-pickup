using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using Cursor = UnityEngine.Cursor;

namespace StonewardsOreAutoPickup
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class OreAutoPickupPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "community.stonewards.oreautopickup";
        public const string PluginName = "Stonewards Ore Auto Pickup";
        public const string PluginVersion = "0.2.2";

        private const float ScanInterval = 0.32f;
        private const float DiscoveryInterval = 4f;
        private const float RetryDelay = 7f;
        private const float AckTimeout = 6f;
        private const float ToastDuration = 1.8f;
        private const int MaxVisibleToasts = 4;
        private const int MaxPickupRequestsPerScan = 8;
        private const float WindowWidth = 516f;
        private const float WindowHeight = 390f;

        // Colors follow Stonewards/PartyHUD: graphite, slate, burgundy, and warm gold.
        private static readonly Color Frame = new Color(0.72f, 0.64f, 0.48f, 1f);
        private static readonly Color Background = new Color(19f / 255f, 23f / 255f, 32f / 255f, 0.99f);
        private static readonly Color Header = new Color(76f / 255f, 46f / 255f, 48f / 255f, 1f);
        private static readonly Color Slot = new Color(0.25f, 0.32f, 0.38f, 1f);
        private static readonly Color MutedSlot = new Color(0.15f, 0.19f, 0.24f, 1f);
        private static readonly Color Gold = new Color(240f / 255f, 200f / 255f, 94f / 255f, 1f);
        private static readonly Color Text = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color MutedText = new Color(0.69f, 0.72f, 0.76f, 1f);
        private static readonly Color Divider = new Color(0.71f, 0.63f, 0.48f, 0.42f);

        private sealed class PendingPickup
        {
            public int LastCount;
            public float Deadline;
        }

        private sealed class PickupToast
        {
            public string Id;
            public string Name;
            public int Amount;
            public Color Tint;
            public float Expires;
        }

        private sealed class OreOption
        {
            public string Id;
            public string Name;
            public Sprite Icon;
            public Color Tint;
            public ConfigEntry<bool> AutoPick;
        }

        private readonly Dictionary<string, OreOption> _oresById =
            new Dictionary<string, OreOption>(StringComparer.Ordinal);
        private readonly List<OreOption> _orderedOres = new List<OreOption>();
        private readonly Dictionary<int, float> _recentAttempts = new Dictionary<int, float>();
        private readonly Dictionary<string, PendingPickup> _pendingPickups =
            new Dictionary<string, PendingPickup>(StringComparer.Ordinal);
        private readonly List<PickupToast> _pickupToasts = new List<PickupToast>();
        private static readonly MethodInfo InventoryGetItemCountMethod =
            typeof(InventorySystem).GetMethod("GetItemCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
        private bool _warnedAboutInventoryCount;
        private static readonly BindingFlags AllInstance = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo ResourceClustersField =
            typeof(DigMatterProfileSO).GetField("resourceClusters", AllInstance);
        private readonly Dictionary<Type, FieldInfo> _clusterItemFields =
            new Dictionary<Type, FieldInfo>();

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _radius;
        private ConfigEntry<bool> _useManualPickup;
        private ConfigEntry<KeyboardShortcut> _settingsKey;
        private bool _menuOpen;
        private bool _oreWindowOpen;
        private bool _captureShortcut;
        private bool _ownsInputState;
        private InputManager.InputState _returnInputState;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private float _nextDiscovery;
        private float _nextScan;
        private float _nextCleanup;
        private Vector2 _scroll;
        private string _oreSearch = "";
        private string _notice = "";
        private bool _warnedNoProfileField;
        private bool _loggedDiscovery;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _miniStyle;
        private GUIStyle _searchStyle;
        private GUIStyle _smallGoldStyle;
        private GUIStyle _pickupToastStyle;
        private Font _nativeUiFont;
        private float _nextFontLookup;
        private const float OreWindowWidth = 572f;
        private const float OreWindowHeight = 616f;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true,
                "Automatically collect only the selected mined resources.");
            _radius = Config.Bind("General", "PickupRadius", 2.5f,
                new ConfigDescription("Pickup radius in meters; avoid values larger than native interaction range.",
                    new AcceptableValueRange<float>(1f, 4f)));
            _useManualPickup = Config.Bind("Advanced", "UseManualPickupRoute", true,
                "Use the native manual pickup command for full-stack collection. Set false to restore the previous game's auto-pickup command if needed.");
            _settingsKey = Config.Bind("Hotkeys", "ToggleSettings",
                new KeyboardShortcut(KeyCode.F10),
                "Open/close the built-in ore selection menu. Ctrl/Shift/Alt combinations are supported.");
            Logger.LogInfo("Ore Auto Pickup v" + PluginVersion + " initialized.");
        }

        private void Update()
        {
            if (_menuOpen && !_captureShortcut &&
                (_settingsKey.Value.IsDown() || Input.GetKeyDown(KeyCode.Escape)))
            {
                CloseMenu();
            }
            else if (!_menuOpen && _settingsKey.Value.IsDown())
            {
                OpenMenu();
            }

            if (_menuOpen)
            {
                // A native menu or level transition can take over the input manager.
                var manager = InputManager.Instance;
                if (manager == null ||
                    manager.CurrentInputState != InputManager.InputState.GeneralMenu)
                {
                    CloseMenu(false);
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            // Show only quantities confirmed by the local player's inventory.
            CheckPendingPickupCounts();

            if (Time.unscaledTime >= _nextDiscovery)
            {
                _nextDiscovery = Time.unscaledTime + DiscoveryInterval;
                DiscoverOres();
            }

            if (!_enabled.Value || _menuOpen || Time.unscaledTime < _nextScan)
                return;

            _nextScan = Time.unscaledTime + ScanInterval;
            TryPickupNearbyOres();
        }

        private void DiscoverOres()
        {
            if (ResourceClustersField == null)
            {
                if (!_warnedNoProfileField)
                {
                    Logger.LogWarning("DigMatterProfileSO.resourceClusters is unavailable. No ore type is assumed or guessed.");
                    _warnedNoProfileField = true;
                }
                return;
            }
            try
            {
                var profiles = Resources.FindObjectsOfTypeAll<DigMatterProfileSO>();
                var changed = false;
                foreach (var profile in profiles)
                {
                    if (profile == null)
                        continue;
                    var clusters = ResourceClustersField.GetValue(profile) as IEnumerable;
                    if (clusters == null)
                        continue;

                    foreach (var cluster in clusters)
                    {
                        if (cluster == null)
                            continue;
                        var clusterType = cluster.GetType();
                        if (!_clusterItemFields.TryGetValue(clusterType, out var itemField))
                        {
                            itemField = clusterType.GetField("resourceCluster", AllInstance);
                            _clusterItemFields[clusterType] = itemField;
                        }
                        var item = itemField == null ? null : itemField.GetValue(cluster) as ItemDataSO;
                        if (item == null || item.isScrapItem || string.IsNullOrEmpty(item.itemID) ||
                            _oresById.ContainsKey(item.itemID))
                            continue;

                        string id = item.itemID;
                        string name = id;
                        try
                        {
                            var localized = item.GetLocalizedName();
                            if (!string.IsNullOrEmpty(localized))
                                name = localized;
                        }
                        catch (Exception) { /* Localization may not be available in the hub. */ }

                        // Each stable game item ID gets its own persisted BepInEx setting.
                        var option = new OreOption
                        {
                            Id = id,
                            Name = name,
                            Icon = item.itemIcon,
                            Tint = OreTint(id + " " + name, item.Rarity, item.itemIcon),
                            AutoPick = Config.Bind("Ores", id, true,
                                "Automatically collect the mined resource " + name + " (" + id + ").")
                        };
                        _oresById.Add(id, option);
                        _orderedOres.Add(option);
                        Logger.LogInfo("Ore UI color: " + id + " #" +
                            ColorUtility.ToHtmlStringRGB(option.Tint));
                        changed = true;
                    }
                }
                if (changed)
                {
                    _orderedOres.Sort((a, b) =>
                        string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    Config.Save();
                    Logger.LogInfo("Found " + _orderedOres.Count + " mined resource types: " +
                        string.Join(", ", _orderedOres.ConvertAll(x => x.Id).ToArray()));
                    _loggedDiscovery = true;
                }
                else if (!_loggedDiscovery && _orderedOres.Count == 0)
                {
                    Logger.LogInfo("Ore data not loaded yet; enter a level to discover actual mined resources.");
                    _loggedDiscovery = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Ore discovery failed: " + ex.Message);
            }
        }

        private static FirstPersonController LocalPlayer()
        {
            var players = FirstPersonController.LocalPlayers;
            if (players == null)
                return null;
            foreach (var player in players)
                if (player != null && player.isLocalPlayer)
                    return player;
            return null;
        }

        private void TryPickupNearbyOres()
        {
            if (_oresById.Count == 0 || !NetworkClient.active)
                return;
            var input = InputManager.Instance;
            if (input == null || input.CurrentInputState != InputManager.InputState.Gameplay)
                return;
            var player = LocalPlayer();
            if (player == null || player.PlayerStats == null || player.PlayerStats.isDead)
                return;

            var radiusSquared = _radius.Value * _radius.Value;
            var playerPosition = player.transform.position;
            int requested = 0;
            try
            {
                var pickups = UnityEngine.Object.FindObjectsOfType<PickableItem>();
                foreach (var pickup in pickups)
                {
                    if (pickup == null || !pickup.isActiveAndEnabled || pickup.NetworkSyncPlayerID != 0)
                        continue;
                    var item = pickup.ItemDataSO;
                    if (item == null || string.IsNullOrEmpty(item.itemID) ||
                        !_oresById.TryGetValue(item.itemID, out var option) ||
                        !option.AutoPick.Value)
                        continue;

                    if ((pickup.transform.position - playerPosition).sqrMagnitude > radiusSquared)
                        continue;

                    var instanceId = pickup.GetInstanceID();
                    if (_recentAttempts.TryGetValue(instanceId, out var nextAllowed) &&
                        Time.unscaledTime < nextAllowed)
                        continue;
                    _recentAttempts[instanceId] = Time.unscaledTime + RetryDelay;

                    // Take an inventory snapshot BEFORE asking the server for a pickup.
                    // Notification is emitted later, only when inventory count rises.
                    TrackRequestedPickup(player, item.itemID);

                    // Native command: the host remains authoritative about inventory and pickup lifetime.
                    // NEVER use CmdForcePickupItem (it can create items without consuming a world drop).
                    // Route through the same command as a regular manual pickup. Unlike the
                    // legacy auto-pickup route, this is intended to consume the complete
                    // world stack with the game's inventory-capacity checks.
                    if (_useManualPickup.Value)
                        player.CmdPickupItem(player, pickup);
                    else
                        player.CmdAutoPickupItem(player, pickup);
                    Logger.LogDebug("Pickup request: item=" + item.itemID +
                        ", world object=" + pickup.GetInstanceID() +
                        ", route=" + (_useManualPickup.Value ? "manual" : "auto"));
                    requested++;
                    if (requested >= MaxPickupRequestsPerScan)
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Pickup scan failed: " + ex.Message);
            }

            // Prune expired entries, rather than clearing the entire cooldown table
            // and resending pickup commands for still-present world objects.
            if (Time.unscaledTime > _nextCleanup)
            {
                _nextCleanup = Time.unscaledTime + 30f;
                var expired = new List<int>();
                foreach (var entry in _recentAttempts)
                    if (entry.Value + 30f < Time.unscaledTime)
                        expired.Add(entry.Key);
                foreach (var id in expired)
                    _recentAttempts.Remove(id);
            }
        }


        private int ReadInventoryCount(FirstPersonController player, string itemId)
        {
            if (player == null || player.InventorySystem == null ||
                InventoryGetItemCountMethod == null)
            {
                if (InventoryGetItemCountMethod == null && !_warnedAboutInventoryCount)
                {
                    _warnedAboutInventoryCount = true;
                    Logger.LogWarning("InventorySystem.GetItemCount(string) unavailable. Pickup notifications will not guess amounts.");
                }
                return -1;
            }
            try
            {
                return Convert.ToInt32(InventoryGetItemCountMethod.Invoke(
                    player.InventorySystem, new object[] { itemId }));
            }
            catch (Exception ex)
            {
                if (!_warnedAboutInventoryCount)
                {
                    _warnedAboutInventoryCount = true;
                    Logger.LogWarning("Unable to read confirmed inventory counts: " + ex.Message);
                }
                return -1;
            }
        }

        private void TrackRequestedPickup(FirstPersonController player, string id)
        {
            int count = ReadInventoryCount(player, id);
            if (count < 0)
                return;
            if (_pendingPickups.TryGetValue(id, out var existing))
            {
                // Keep the baseline when several ground stacks of the same
                // material are requested before the network response arrives.
                existing.Deadline = Time.unscaledTime + AckTimeout;
            }
            else
            {
                _pendingPickups.Add(id, new PendingPickup
                {
                    LastCount = count,
                    Deadline = Time.unscaledTime + AckTimeout
                });
            }
        }

        private void CheckPendingPickupCounts()
        {
            if (_pendingPickups.Count == 0)
                return;
            var player = LocalPlayer();
            if (player == null || !NetworkClient.active)
            {
                _pendingPickups.Clear();
                return;
            }

            var completed = new List<string>();
            foreach (var pair in _pendingPickups)
            {
                var pending = pair.Value;
                if (Time.unscaledTime > pending.Deadline)
                {
                    completed.Add(pair.Key);
                    continue;
                }

                int current = ReadInventoryCount(player, pair.Key);
                if (current < 0)
                    continue;

                int received = current - pending.LastCount;
                // Do not show the world's pickup size or a guessed amount:
                // synced inventory growth is our only numeric source of truth.
                if (received > 0 && _oresById.TryGetValue(pair.Key, out var ore))
                {
                    PushPickupToast(ore, received);
                    Logger.LogDebug("Confirmed pickup: " + pair.Key +
                        " +" + received + " (" + pending.LastCount + " -> " + current + ")");
                }
                pending.LastCount = current;
            }
            foreach (var id in completed)
                _pendingPickups.Remove(id);
        }

        private void PushPickupToast(OreOption ore, int received)
        {
            // Merge very recent notifications of the same ore into one line.
            foreach (var toast in _pickupToasts)
            {
                if (toast.Id == ore.Id && toast.Expires - Time.unscaledTime > 0.65f)
                {
                    toast.Amount += received;
                    toast.Expires = Time.unscaledTime + ToastDuration;
                    return;
                }
            }
            _pickupToasts.Add(new PickupToast
            {
                Id = ore.Id,
                Name = ore.Name,
                Tint = ore.Tint,
                Amount = received,
                Expires = Time.unscaledTime + ToastDuration
            });
            while (_pickupToasts.Count > MaxVisibleToasts)
                _pickupToasts.RemoveAt(0);
        }

        private void DrawPickupToasts()
        {
            for (int i = _pickupToasts.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= _pickupToasts[i].Expires)
                    _pickupToasts.RemoveAt(i);
            if (_pickupToasts.Count == 0)
                return;

            EnsureStyles();
            var oldDepth = GUI.depth;
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            var oldContentColor = GUI.contentColor;
            try
            {
                GUI.depth = -540;
                float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.5f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float center = Screen.width / scale * 0.5f;
                float top = Screen.height / scale * 0.322f;
                for (int i = 0; i < _pickupToasts.Count; i++)
                {
                    var toast = _pickupToasts[i];
                    float timeLeft = toast.Expires - Time.unscaledTime;
                    float alpha = Mathf.Clamp01(timeLeft / 0.42f);
                    // GUI.contentColor directly tints IMGUI text regardless of
                    // the active skin's normal text color.
                    GUI.color = Color.white;
                    _pickupToastStyle.normal.textColor = Color.white;
                    GUI.contentColor = new Color(toast.Tint.r, toast.Tint.g,
                        toast.Tint.b, alpha);
                    GUI.Label(new Rect(center - 190f, top + i * 37f, 380f, 36f),
                        "+" + toast.Amount + " " + toast.Name.ToUpperInvariant(),
                        _pickupToastStyle);
                }
            }
            finally
            {
                GUI.depth = oldDepth;
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.contentColor = oldContentColor;
            }
        }

        /// <summary>
        /// Prefer the actual item sprite, not item rarity. Most basic ores share
        /// the same rarity, so the old rarity-based fallback made them identical.
        /// Texture readback is deliberately CPU-only and one-time per ore; some
        /// game atlases are non-readable, so safely fall back to material names.
        /// </summary>
        private static Color OreTint(string label, Rarity rarity, Sprite icon)
        {
            string name = (label ?? "").ToLowerInvariant();
            if (name.Contains("gold") || name.Contains("золот"))
                return new Color(1f, 0.78f, 0.24f);
            if (name.Contains("silver") || name.Contains("сереб"))
                return new Color(0.72f, 0.89f, 1f);
            if (name.Contains("copper") || name.Contains("медн") || name.Contains("медь"))
                return new Color(1f, 0.48f, 0.24f);
            if (name.Contains("iron") || name.Contains("желез"))
                return new Color(0.65f, 0.77f, 0.90f);
            if (name.Contains("tin") || name.Contains("олов"))
                return new Color(0.72f, 0.82f, 0.96f);
            if (name.Contains("platinum") || name.Contains("платин"))
                return new Color(0.53f, 0.90f, 0.99f);
            if (name.Contains("titanium") || name.Contains("титан"))
                return new Color(0.83f, 0.73f, 0.95f);
            if (name.Contains("cobalt") || name.Contains("кобальт"))
                return new Color(0.43f, 0.68f, 1f);
            if (name.Contains("coal") || name.Contains("угол") || name.Contains("уголь"))
                return new Color(0.65f, 0.69f, 0.75f);
            if (name.Contains("stone") || name.Contains("камен") || name.Contains("камень"))
                return new Color(0.73f, 0.70f, 0.59f);
            if (name.Contains("sulfur") || name.Contains("sulphur") || name.Contains("сера"))
                return new Color(1f, 0.93f, 0.30f);
            if (name.Contains("uranium") || name.Contains("уран"))
                return new Color(0.55f, 1f, 0.36f);
            if (name.Contains("emerald") || name.Contains("изумруд"))
                return new Color(0.36f, 1f, 0.62f);
            if (name.Contains("ruby") || name.Contains("рубин"))
                return new Color(1f, 0.39f, 0.46f);
            if (name.Contains("sapphire") || name.Contains("сапфир"))
                return new Color(0.43f, 0.67f, 1f);
            if (name.Contains("amethyst") || name.Contains("аметист") ||
                name.Contains("crystal") || name.Contains("кристалл"))
                return new Color(0.79f, 0.50f, 1f);
            if (name.Contains("diamond") || name.Contains("алмаз"))
                return new Color(0.50f, 0.95f, 1f);
            if (name.Contains("obsidian") || name.Contains("обсидиан"))
                return new Color(0.67f, 0.51f, 0.85f);

            // For unfamiliar materials, sample the ore's actual icon when
            // readable instead of falling back to a shared rarity color.
            Color sampled;
            if (TrySampleIconColor(icon, out sampled))
                return sampled;

            // Unknown game or modded ores must still be visually distinguishable
            // even when they share a rarity. Stable per-item ID, not Random or
            // rarity, determines their high-contrast hue across sessions.
            uint hash = 2166136261u;
            foreach (char character in name)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            float hue = (hash % 360u) / 360f;
            return Color.HSVToRGB(hue, 0.67f, 1f);
        }

        private static bool TrySampleIconColor(Sprite sprite, out Color tint)
        {
            tint = Color.white;
            if (sprite == null || sprite.texture == null)
                return false;

            var texture = sprite.texture;
            if (!texture.isReadable)
                return false;

            try
            {
                var spriteRect = sprite.textureRect;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(spriteRect.xMin), 0, texture.width - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(spriteRect.xMax), x0 + 1, texture.width);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(spriteRect.yMin), 0, texture.height - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(spriteRect.yMax), y0 + 1, texture.height);
                int stepX = Mathf.Max(1, (x1 - x0) / 16);
                int stepY = Mathf.Max(1, (y1 - y0) / 16);
                float sumR = 0f, sumG = 0f, sumB = 0f, total = 0f;

                for (int y = y0; y < y1; y += stepY)
                {
                    for (int x = x0; x < x1; x += stepX)
                    {
                        Color pixel = texture.GetPixel(x, y);
                        if (pixel.a < 0.45f)
                            continue;

                        float highest = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                        float lowest = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
                        float saturation = highest <= 0f ? 0f : (highest - lowest) / highest;
                        if (highest < 0.18f)
                            continue;

                        // Prefer recognizable material colors over black outlines.
                        float weight = pixel.a * (0.35f + saturation * 1.5f) * highest;
                        sumR += pixel.r * weight;
                        sumG += pixel.g * weight;
                        sumB += pixel.b * weight;
                        total += weight;
                    }
                }

                if (total < 0.001f)
                    return false;

                var avg = new Color(sumR / total, sumG / total, sumB / total);
                float hue, sat, value;
                Color.RGBToHSV(avg, out hue, out sat, out value);

                // Pale/metallic materials should stay silver rather than
                // acquiring an arbitrary bright hue due to texture noise.
                if (sat < 0.12f)
                    return false; // Let the ID/name-specific palette decide.

                tint = Color.HSVToRGB(hue, Mathf.Clamp(sat * 1.28f, 0.45f, 0.87f),
                    Mathf.Clamp(value * 1.24f, 0.85f, 1f));
                return true;
            }
            catch (Exception)
            {
                // Packed or otherwise unsupported sprites fall back to their
                // known resource name or a stable item-ID palette.
                return false;
            }
        }

        private void OpenMenu()
        {
            var input = InputManager.Instance;
            if (input == null || GameManager.Instance == null)
                return;
            var state = input.CurrentInputState;
            if (state != InputManager.InputState.Gameplay &&
                state != InputManager.InputState.Dead)
                return;
            _returnInputState = state;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _menuOpen = true;
            _oreWindowOpen = false;
            _ownsInputState = true;
            _captureShortcut = false;
            _notice = "";
            DiscoverOres();
            input.SetState(InputManager.InputState.GeneralMenu);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseMenu(bool restoreInput = true)
        {
            if (!_menuOpen)
                return;
            _menuOpen = false;
            _oreWindowOpen = false;
            _captureShortcut = false;
            var input = InputManager.Instance;
            if (_ownsInputState && input != null &&
                input.CurrentInputState == InputManager.InputState.GeneralMenu && restoreInput)
            {
                var local = LocalPlayer();
                var state = local != null && local.PlayerStats != null
                    ? (local.PlayerStats.isDead ? InputManager.InputState.Dead : InputManager.InputState.Gameplay)
                    : _returnInputState;
                input.SetState(state);
            }
            _ownsInputState = false;
            if (restoreInput)
            {
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }
        }

        private void OnDisable() { CloseMenu(); }
        private void OnDestroy() { CloseMenu(); }

        private static void Fill(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Panel(Rect r, Color background)
        {
            Fill(r, Frame);
            Fill(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), background);
        }

        private bool Button(Rect r, string content, bool selected = false)
        {
            Fill(r, selected ? Header : Slot);
            Fill(new Rect(r.x, r.y, r.width, 1f), selected ? Gold : Frame);
            Fill(new Rect(r.x, r.y + r.height - 1f, r.width, 1f), Frame);
            return GUI.Button(r, content, _buttonStyle);
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = MakeStyle(24, Gold, TextAnchor.MiddleLeft);
                _labelStyle = MakeStyle(18, Text, TextAnchor.MiddleLeft);
                _mutedStyle = MakeStyle(15, MutedText, TextAnchor.MiddleLeft);
                _mutedStyle.wordWrap = true;
                _smallGoldStyle = MakeStyle(16, Gold, TextAnchor.MiddleLeft);
                _pickupToastStyle = MakeStyle(26, Text, TextAnchor.MiddleCenter);
                _miniStyle = MakeStyle(14, MutedText, TextAnchor.MiddleRight);
                _searchStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Text },
                    focused = { textColor = Text }
                };
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { background = null, textColor = Text },
                    hover = { background = null, textColor = Gold },
                    active = { background = null, textColor = Gold },
                    focused = { background = null, textColor = Text }
                };
            }
            TryApplyNativeFont();
        }

        private static GUIStyle MakeStyle(int size, Color color, TextAnchor align)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Normal,
                alignment = align,
                clipping = TextClipping.Clip,
                richText = false
            };
            style.normal.textColor = color;
            return style;
        }

        private void TryApplyNativeFont()
        {
            if (_nativeUiFont != null || Time.unscaledTime < _nextFontLookup)
                return;
            _nextFontLookup = Time.unscaledTime + 8f;
            Font primary = null;
            Font fallback = null;
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font == null)
                    continue;
                if (font.name == "Jersey10-Regular")
                    primary = font;
                else if (font.name == "Jersey10-Regular2")
                    fallback = font;
            }
            _nativeUiFont = primary != null ? primary : fallback;
            if (_nativeUiFont == null)
                return;
            foreach (var style in new[] {
                _titleStyle, _labelStyle, _mutedStyle, _smallGoldStyle,
                _miniStyle, _searchStyle, _buttonStyle, _pickupToastStyle })
            {
                style.font = _nativeUiFont;
                style.fontStyle = FontStyle.Normal;
            }
            Logger.LogInfo("Ore Auto Pickup native UI font: " + _nativeUiFont.name);
        }

        private void OnGUI()
        {
            if (Event.current == null)
                return;
            if (Event.current.type == EventType.Repaint)
                DrawPickupToasts();
            if (!_menuOpen)
                return;
            EnsureStyles();
            var oldMatrix = GUI.matrix;
            var oldDepth = GUI.depth;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1120f, Screen.height / 740f), 0.65f, 1.25f);
            try
            {
                GUI.depth = -550;
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float width = _oreWindowOpen ? OreWindowWidth : WindowWidth;
                float height = _oreWindowOpen ? OreWindowHeight : WindowHeight;
                float x = Mathf.Max(0f, (Screen.width / scale - width) * 0.5f);
                float y = Mathf.Max(0f, (Screen.height / scale - height) * 0.5f);
                DrawMenu(x, y);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.depth = oldDepth;
            }
        }

        private void DrawMenu(float x, float y)
        {
            if (_oreWindowOpen)
            {
                DrawOreSelection(x, y);
                return;
            }

            Panel(new Rect(x, y, WindowWidth, WindowHeight), Background);
            Fill(new Rect(x + 2f, y + 2f, WindowWidth - 4f, 58f), Header);
            Fill(new Rect(x + 2f, y + 58f, WindowWidth - 4f, 2f), Gold);
            GUI.Label(new Rect(x + 20f, y + 10f, 392f, 38f),
                "ORE AUTO PICKUP", _titleStyle);
            if (Button(new Rect(x + WindowWidth - 55f, y + 14f, 32f, 31f), "X"))
            {
                CloseMenu();
                return;
            }

            GUI.Label(new Rect(x + 20f, y + 73f, 292f, 31f),
                "Auto pickup", _labelStyle);
            if (Button(new Rect(x + 341f, y + 74f, 150f, 31f),
                _enabled.Value ? "ENABLED" : "DISABLED", _enabled.Value))
            {
                _enabled.Value = !_enabled.Value;
                Config.Save();
            }

            GUI.Label(new Rect(x + 20f, y + 117f, 226f, 31f),
                "Pickup radius", _labelStyle);
            float newRadius = GUI.HorizontalSlider(
                new Rect(x + 242f, y + 126f, 166f, 18f), _radius.Value, 1f, 4f);
            if (Mathf.Abs(newRadius - _radius.Value) > 0.05f)
            {
                _radius.Value = (float)Math.Round(newRadius * 10f) / 10f;
                Config.Save();
            }
            GUI.Label(new Rect(x + 421f, y + 116f, 79f, 31f),
                _radius.Value.ToString("0.0") + " m", _smallGoldStyle);

            Fill(new Rect(x + 20f, y + 162f, WindowWidth - 40f, 1f), Divider);
            GUI.Label(new Rect(x + 20f, y + 170f, 263f, 33f),
                "Ore selection", _labelStyle);
            int selectedCount = 0;
            foreach (var ore in _orderedOres)
                if (ore.AutoPick.Value)
                    selectedCount++;
            if (Button(new Rect(x + 291f, y + 170f, 200f, 34f),
                "CONFIGURE ORES  >"))
            {
                _scroll = Vector2.zero;
                _oreWindowOpen = true;
                DiscoverOres();
            }
            GUI.Label(new Rect(x + 20f, y + 203f, WindowWidth - 40f, 25f),
                selectedCount + " / " + _orderedOres.Count + " resource types enabled",
                _mutedStyle);
            Fill(new Rect(x + 20f, y + 239f, WindowWidth - 40f, 1f), Divider);

            GUI.Label(new Rect(x + 20f, y + 250f, 255f, 30f),
                "Settings shortcut", _labelStyle);
            if (Button(new Rect(x + 281f, y + 250f, 210f, 31f),
                _captureShortcut ? "PRESS KEY..." : _settingsKey.Value.ToString(),
                _captureShortcut))
            {
                _captureShortcut = true;
                _notice = "Press Esc to cancel";
            }
            if (_captureShortcut)
                CaptureShortcut();
            GUI.Label(new Rect(x + 20f, y + 286f, WindowWidth - 40f, 35f),
                "Nearby mined ore only. Other items use vanilla pickup.",
                _mutedStyle);
            if (!string.IsNullOrEmpty(_notice))
                GUI.Label(new Rect(x + 20f, y + 323f, WindowWidth - 40f, 22f),
                    _notice, _smallGoldStyle);
            if (Button(new Rect(x + 20f, y + 354f, WindowWidth - 40f, 28f),
                "CLOSE SETTINGS"))
                CloseMenu();
        }

        private void DrawOreSelection(float x, float y)
        {
            Panel(new Rect(x, y, OreWindowWidth, OreWindowHeight), Background);
            Fill(new Rect(x + 2f, y + 2f, OreWindowWidth - 4f, 58f), Header);
            Fill(new Rect(x + 2f, y + 58f, OreWindowWidth - 4f, 2f), Gold);
            GUI.Label(new Rect(x + 20f, y + 11f, 420f, 37f),
                "ORE PICKUP FILTER", _titleStyle);
            if (Button(new Rect(x + OreWindowWidth - 56f, y + 14f, 34f, 31f), "X"))
            {
                _oreWindowOpen = false;
                return;
            }

            GUI.Label(new Rect(x + 20f, y + 72f, OreWindowWidth - 40f, 33f),
                "Choose which mined resources go into your inventory.", _mutedStyle);
            if (Button(new Rect(x + 20f, y + 112f, 152f, 33f), "ENABLE ALL"))
                SetAll(true);
            if (Button(new Rect(x + 181f, y + 112f, 154f, 33f), "DISABLE ALL"))
                SetAll(false);
            GUI.Label(new Rect(x + 351f, y + 112f, 198f, 33f),
                _orderedOres.Count + " discovered", _miniStyle);

            GUI.Label(new Rect(x + 20f, y + 160f, 91f, 29f), "FIND", _labelStyle);
            Fill(new Rect(x + 99f, y + 161f, OreWindowWidth - 120f, 29f), Slot);
            _oreSearch = GUI.TextField(new Rect(x + 106f, y + 160f, OreWindowWidth - 135f, 31f),
                _oreSearch, 60, _searchStyle);

            var visible = new List<OreOption>();
            foreach (var ore in _orderedOres)
                if (string.IsNullOrWhiteSpace(_oreSearch) ||
                    ore.Name.IndexOf(_oreSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ore.Id.IndexOf(_oreSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    visible.Add(ore);

            var area = new Rect(x + 20f, y + 202f, OreWindowWidth - 40f, 315f);
            Panel(area, MutedSlot);
            float contentHeight = Mathf.Max(area.height - 7f, visible.Count * 57f + 13f);
            _scroll = GUI.BeginScrollView(
                new Rect(area.x + 3f, area.y + 3f, area.width - 6f, area.height - 6f),
                _scroll, new Rect(0f, 0f, area.width - 26f, contentHeight));
            if (_orderedOres.Count == 0)
                GUI.Label(new Rect(12f, 12f, area.width - 42f, 82f),
                    "Ore types are not available yet. Enter a level and mine a resource.",
                    _mutedStyle);
            else if (visible.Count == 0)
                GUI.Label(new Rect(12f, 12f, area.width - 42f, 52f),
                    "Nothing matches the filter.", _mutedStyle);

            for (int i = 0; i < visible.Count; i++)
            {
                var ore = visible[i];
                float ry = i * 57f + 8f;
                Fill(new Rect(8f, ry, area.width - 33f, 51f), Slot);
                DrawOreIcon(ore.Icon, new Rect(16f, ry + 5f, 41f, 41f));
                var previous = GUI.contentColor;
                GUI.contentColor = ore.Tint;
                GUI.Label(new Rect(71f, ry + 5f, area.width - 237f, 40f),
                    ore.Name, _labelStyle);
                GUI.contentColor = previous;
                if (Button(new Rect(area.width - 150f, ry + 8f, 112f, 33f),
                    ore.AutoPick.Value ? "PICK UP" : "IGNORE", ore.AutoPick.Value))
                {
                    ore.AutoPick.Value = !ore.AutoPick.Value;
                    Config.Save();
                    _notice = ore.Name + (ore.AutoPick.Value ? " enabled" : " disabled");
                }
            }
            GUI.EndScrollView();
            Fill(new Rect(x + 20f, y + 530f, OreWindowWidth - 40f, 1f), Divider);
            GUI.Label(new Rect(x + 20f, y + 539f, OreWindowWidth - 40f, 26f),
                string.IsNullOrEmpty(_notice) ? "Changes are saved immediately." : _notice,
                _mutedStyle);
            if (Button(new Rect(x + 20f, y + 576f, OreWindowWidth - 40f, 29f),
                "<  BACK TO SETTINGS"))
                _oreWindowOpen = false;
        }

        private static void DrawOreIcon(Sprite icon, Rect rect)
        {
            if (icon == null || icon.texture == null)
            {
                Panel(rect, MutedSlot);
                return;
            }
            try
            {
                var texture = icon.texture;
                var spriteRect = icon.textureRect;
                var uv = new Rect(
                    spriteRect.x / texture.width, spriteRect.y / texture.height,
                    spriteRect.width / texture.width, spriteRect.height / texture.height);
                GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            }
            catch (Exception)
            {
                // Some sprites are tightly packed. Render a neutral fallback.
                Panel(rect, MutedSlot);
            }
        }

        private void SetAll(bool value)
        {
            foreach (var option in _orderedOres)
                option.AutoPick.Value = value;
            Config.Save();
            _notice = value ? "All mined resources enabled" : "All mined resources disabled";
        }

        private void CaptureShortcut()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;
            if (e.keyCode == KeyCode.Escape)
            {
                _captureShortcut = false;
                _notice = "Shortcut change canceled";
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.None || IsModifier(e.keyCode))
                return;
            var modifiers = new List<KeyCode>(3);
            if (e.control) modifiers.Add(KeyCode.LeftControl);
            if (e.shift) modifiers.Add(KeyCode.LeftShift);
            if (e.alt) modifiers.Add(KeyCode.LeftAlt);
            _settingsKey.Value = new KeyboardShortcut(e.keyCode, modifiers.ToArray());
            Config.Save();
            _captureShortcut = false;
            _notice = "Shortcut saved";
            e.Use();
        }

        private static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.LeftControl || key == KeyCode.RightControl ||
                key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
                key == KeyCode.LeftShift || key == KeyCode.RightShift;
        }
    }
}
