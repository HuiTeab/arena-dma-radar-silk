using System.Collections.Generic;
using System.IO;

namespace eft_dma_radar.Arena.Config
{
    public sealed class ArenaConfig
    {
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-arena");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        // ── DMA ──────────────────────────────────────────────────────────────

        [JsonPropertyName("device")]
        public string DeviceStr { get; set; } = "fpga";

        [JsonPropertyName("memmapEnabled")]
        public bool MemMapEnabled { get; set; } = false;

        // ── Logging ───────────────────────────────────────────────────────────

        [JsonPropertyName("debugLogging")]
        public bool DebugLogging { get; set; } = false;

        /// <summary>
        /// Enables the IL2CPP class-field dumper output (per-player floods at discovery /
        /// transform reinit / skeleton init). Off by default — only useful when diagnosing
        /// IL2CPP layout drift after a game update. Independent of <see cref="DebugLogging"/>.
        /// </summary>
        [JsonPropertyName("il2cppDump")]
        public bool Il2CppDump { get; set; } = false;

        /// <summary>
        /// Enables <see cref="ExceptionTracer"/> — first-chance VmmException /
        /// BadPtrException hook that logs each unique call site (with full
        /// stack trace) exactly once. Use it to answer "which read is the one
        /// throwing the VmmException I see in the debugger?" Capped at 200
        /// distinct sites per session so it self-limits even when a read
        /// fails on every tick. Independent of <see cref="DebugLogging"/>.
        /// Also togglable via the <c>ARENA_TRACE_DMA_EXCEPTIONS</c> env var
        /// for pre-launch debugging.
        /// </summary>
        [JsonPropertyName("traceDmaExceptions")]
        public bool TraceDmaExceptions { get; set; } = false;

        // ── Window ────────────────────────────────────────────────────────────

        [JsonPropertyName("windowWidth")]
        public int WindowWidth { get; set; } = 1280;

        [JsonPropertyName("windowHeight")]
        public int WindowHeight { get; set; } = 1024;

        [JsonPropertyName("windowMaximized")]
        public bool WindowMaximized { get; set; } = false;

        [JsonPropertyName("targetFps")]
        public int TargetFps { get; set; } = 144;

        [JsonPropertyName("uiScale")]
        public float UIScale { get; set; } = 1.0f;

        // ── Radar UI ──────────────────────────────────────────────────────────

        [JsonPropertyName("zoom")]
        public int Zoom { get; set; } = 100;

        [JsonPropertyName("freeMode")]
        public bool FreeMode { get; set; } = false;

        [JsonPropertyName("showAimlines")]
        public bool ShowAimlines { get; set; } = true;

        [JsonPropertyName("showNames")]
        public bool ShowNames { get; set; } = true;

        [JsonPropertyName("showTeamTag")]
        public bool ShowTeamTag { get; set; } = false;

        [JsonPropertyName("showHeightDiff")]
        public bool ShowHeightDiff { get; set; } = true;

        [JsonPropertyName("showGrid")]
        public bool ShowGrid { get; set; } = true;

        // ── Aimview ───────────────────────────────────────────────────────────

        [JsonPropertyName("aimviewEnabled")]
        public bool AimviewEnabled { get; set; } = false;

        /// <summary>If true, use the live game ViewMatrix via CameraManager.WorldToScreen.</summary>
        [JsonPropertyName("aimviewUseAdvanced")]
        public bool AimviewUseAdvanced { get; set; } = true;

        /// <summary>Hide AI players in the Aimview widget.</summary>
        [JsonPropertyName("aimviewHideAI")]
        public bool AimviewHideAI { get; set; } = false;

        /// <summary>Show name + distance labels under each player dot.</summary>
        [JsonPropertyName("aimviewShowLabels")]
        public bool AimviewShowLabels { get; set; } = true;

        /// <summary>Draw skeleton bone segments on top of player dots when available (advanced mode only).</summary>
        [JsonPropertyName("aimviewDrawSkeletons")]
        public bool AimviewDrawSkeletons { get; set; } = true;

        /// <summary>Maximum render distance (meters).</summary>
        [JsonPropertyName("aimviewMaxDistance")]
        public float AimviewMaxDistance { get; set; } = 300f;

        /// <summary>Synthetic-mode zoom factor (only used when advanced mode is off / unavailable).</summary>
        [JsonPropertyName("aimviewZoom")]
        public float AimviewZoom { get; set; } = 1.0f;

        /// <summary>Eye height offset above the local player root (meters).</summary>
        [JsonPropertyName("aimviewEyeHeight")]
        public float AimviewEyeHeight { get; set; } = 1.5f;

        // ── Game / Camera ─────────────────────────────────────────────────────

        /// <summary>Width of the game's render resolution (used by CameraManager W2S).</summary>
        [JsonPropertyName("gameMonitorWidth")]
        public int GameMonitorWidth { get; set; } = 1920;

        /// <summary>Height of the game's render resolution (used by CameraManager W2S).</summary>
        [JsonPropertyName("gameMonitorHeight")]
        public int GameMonitorHeight { get; set; } = 1080;

        // ── Match Dump ────────────────────────────────────────────────────────

        /// <summary>
        /// When true, pressing the Dump hotkey (F7) or calling
        /// <c>LocalGameWorld.DumpMatchNow()</c> writes a full match snapshot
        /// (JSON + IL2CPP class hierarchy) to the <c>dumps\</c> folder next to the exe.
        /// </summary>
        [JsonPropertyName("enableMatchDump")]
        public bool EnableMatchDump { get; set; } = true;

        // ── ESP Window ────────────────────────────────────────────────────────

        /// <summary>If true, the ESP overlay opens borderless fullscreen; otherwise as a resizable window.</summary>
        [JsonPropertyName("espFullscreen")]
        public bool EspFullscreen { get; set; } = true;

        /// <summary>Last windowed (non-fullscreen) ESP width.</summary>
        [JsonPropertyName("espWindowWidth")]
        public int EspWindowWidth { get; set; } = 1280;

        /// <summary>Last windowed (non-fullscreen) ESP height.</summary>
        [JsonPropertyName("espWindowHeight")]
        public int EspWindowHeight { get; set; } = 720;

        /// <summary>
        /// Index (in <see cref="Silk.NET.Windowing.Monitor.GetMonitors"/> order) of the
        /// monitor the ESP overlay opens on. 0 = primary. Clamped to the available range
        /// at window-open time, so an unplugged monitor falls back gracefully.
        /// </summary>
        [JsonPropertyName("espMonitorIndex")]
        public int EspMonitorIndex { get; set; } = 0;

        // ── Visibility Check ──────────────────────────────────────────────────

        /// <summary>
        /// Unity layers (one-hot bit per layer) treated as see-through by the PhysX raycaster.
        /// Default: 1&lt;&lt;16 — player character colliders, so enemy A's body never blocks the
        /// sightline to enemy B.
        /// </summary>
        [JsonPropertyName("visCheckSeeThroughLayerMask")]
        public uint VisCheckSeeThroughLayerMask { get; set; } = 1u << 16;

        /// <summary>Global actor name substrings classified as see-through on every map.</summary>
        [JsonPropertyName("visCheckGlobalNamePatterns")]
        public string[] VisCheckGlobalNamePatterns { get; set; } = ["Glass", "Cube ("];

        /// <summary>Per-map actor name patterns. Key = MapId (case-insensitive).</summary>
        [JsonPropertyName("visCheckMapNamePatterns")]
        public Dictionary<string, string[]> VisCheckMapNamePatterns { get; set; } = new();

        /// <summary>
        /// Global force-blocker name substrings. Actors matching any of these
        /// are classified as blockers even when the see-through rules (layer
        /// mask or name patterns) would have made them see-through. Inverse
        /// of <see cref="VisCheckGlobalNamePatterns"/>; takes precedence.
        /// </summary>
        [JsonPropertyName("visCheckGlobalBlockerPatterns")]
        public string[] VisCheckGlobalBlockerPatterns { get; set; } = [];

        /// <summary>Per-map force-blocker name patterns. Key = MapId (case-insensitive).</summary>
        [JsonPropertyName("visCheckMapBlockerPatterns")]
        public Dictionary<string, string[]> VisCheckMapBlockerPatterns { get; set; } = new();

        /// <summary>Max ray cast distance (metres). Players beyond this default to visible.</summary>
        [JsonPropertyName("visCheckMaxRayDistance")]
        public float VisCheckMaxRayDistance { get; set; } = 200f;

        /// <summary>Cast a ray to the enemy's head bone.</summary>
        [JsonPropertyName("visCheckBoneHead")]
        public bool VisCheckBoneHead { get; set; } = true;

        /// <summary>Cast a ray to the enemy's chest (spine3) bone.</summary>
        [JsonPropertyName("visCheckBoneChest")]
        public bool VisCheckBoneChest { get; set; } = true;

        /// <summary>Cast a ray to the enemy's pelvis bone.</summary>
        [JsonPropertyName("visCheckBonePelvis")]
        public bool VisCheckBonePelvis { get; set; } = true;

        // ── VisCheck diagnostic logging ──────────────────────────────────────
        // All three default OFF — the logs are useful for offline analysis but
        // a 10k-actor JSONL dump (~3 MB) on every build and a 16 KB/s tick log
        // are not what a casual user wants flushing to disk by default.

        /// <summary>Auto-dump the full PhysX snapshot to JSONL on every SceneCache build/load.</summary>
        [JsonPropertyName("visCheckDiagDumpSnapshot")]
        public bool VisCheckDiagDumpSnapshot { get; set; } = false;

        /// <summary>Append one JSONL line per (tick, player) result to a session-scoped tick log file.</summary>
        [JsonPropertyName("visCheckDiagLogTicks")]
        public bool VisCheckDiagLogTicks { get; set; } = false;

        /// <summary>Append one JSONL line per classifier rule edit + reclassification stats.</summary>
        [JsonPropertyName("visCheckDiagLogClassifier")]
        public bool VisCheckDiagLogClassifier { get; set; } = false;

        // ── Persistence ───────────────────────────────────────────────────────

        public static ArenaConfig Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<ArenaConfig>(json);
                    if (cfg is not null)
                    {
                        Log.WriteLine($"[ArenaConfig] Loaded from {ConfigPath}");
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ArenaConfig] Load failed: {ex.Message} — using defaults.");
            }

            var defaults = new ArenaConfig();
            defaults.Save();
            return defaults;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ArenaConfig] Save failed: {ex.Message}");
            }
        }
    }
}
